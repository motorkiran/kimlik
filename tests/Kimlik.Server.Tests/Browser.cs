using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kimlik.Server.Tests;

internal sealed record WebPage(Uri Url, IHtmlDocument Document)
{
    public string Text => Document.Body?.TextContent ?? string.Empty;
}

/// <summary>
/// Just enough of a browser for the hosted pages: it keeps cookies, lets the test decide which redirects to
/// follow, and submits HTML forms with their hidden fields (anti-forgery tokens, authorization parameters).
/// </summary>
internal sealed class Browser : IDisposable
{
    private static readonly HtmlParser Parser = new();

    private readonly HttpClient _client;

    public Browser(WebApplicationFactory<Program> server, string? acceptLanguage = null)
    {
        _client = server.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri(TestConfiguration.PublicUrl),
        });

        if (acceptLanguage is not null)
        {
            _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(acceptLanguage);
        }
    }

    public HttpClient Client => _client;

    public Task<HttpResponseMessage> GetAsync(string url) => _client.GetAsync(url, TestContext.Current.CancellationToken);

    public async Task<WebPage> GetPageAsync(string url)
    {
        using var response = await GetAsync(url);
        return await ReadPageAsync(response);
    }

    /// <summary>Follows redirects within Kimlik and stops at the first page or at a redirect to another site.</summary>
    public async Task<HttpResponseMessage> FollowAsync(HttpResponseMessage response)
    {
        while (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther
            && response.Headers.Location is { } location
            && (!location.IsAbsoluteUri || location.Host == "localhost"))
        {
            var next = await GetAsync(location.ToString());
            response.Dispose();
            response = next;
        }

        return response;
    }

    public async Task<HttpResponseMessage> SubmitAsync(WebPage page, IReadOnlyDictionary<string, string>? values = null, string formSelector = "form", (string Name, string Value)? submitter = null)
    {
        var form = page.Document.QuerySelector<IHtmlFormElement>(formSelector)
            ?? throw new InvalidOperationException($"No form matches '{formSelector}' on {page.Url}.");

        var fields = new List<KeyValuePair<string, string>>();
        foreach (var input in form.QuerySelectorAll<IHtmlInputElement>("input[name]"))
        {
            if (input.Type is "checkbox" or "radio" && !input.IsChecked)
            {
                continue;
            }

            fields.Add(new(input.Name!, input.Value));
        }

        foreach (var select in form.QuerySelectorAll<IHtmlSelectElement>("select[name]"))
        {
            fields.Add(new(select.Name!, select.Value ?? string.Empty));
        }

        foreach (var (name, value) in values ?? new Dictionary<string, string>())
        {
            fields.RemoveAll(field => field.Key == name);
            fields.Add(new(name, value));
        }

        if (submitter is { } button)
        {
            fields.Add(new(button.Name, button.Value));
        }

        var action = form.GetAttribute("action") is { Length: > 0 } explicitAction ? new Uri(page.Url, explicitAction) : page.Url;
        using var content = new FormUrlEncodedContent(fields);
        return await _client.PostAsync(action, content, TestContext.Current.CancellationToken);
    }

    public static async Task<WebPage> ReadPageAsync(HttpResponseMessage response, HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        if (response.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException($"Expected a page from {response.RequestMessage?.RequestUri} but got {(int)response.StatusCode}.");
        }

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return new WebPage(response.RequestMessage!.RequestUri!, await Parser.ParseDocumentAsync(html, TestContext.Current.CancellationToken));
    }

    public void Dispose() => _client.Dispose();
}
