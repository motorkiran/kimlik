using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Kimlik.Server.Tests.Passkeys;

/// <summary>What passkeys.js does in a browser, for the hosted pages' passkey forms.</summary>
internal static class PasskeyForms
{
    /// <summary>
    /// Fetches the options of the passkey form, has <paramref name="answer"/> respond to them as an authenticator
    /// would, and posts the answer with the form.
    /// </summary>
    public static async Task<HttpResponseMessage> RunPasskeyFormAsync(
        this Browser browser, WebPage page, string formSelector, Func<JsonElement, string> answer, IReadOnlyDictionary<string, string>? values = null)
    {
        var (options, state) = await browser.FetchPasskeyOptionsAsync(page, formSelector);
        var fields = new Dictionary<string, string>(values ?? new Dictionary<string, string>()) { ["Credential"] = answer(options), ["State"] = state };
        return await browser.SubmitAsync(page, fields, formSelector);
    }

    /// <summary>The options and the state that the passkey form's options handler returns.</summary>
    public static async Task<(JsonElement Options, string State)> FetchPasskeyOptionsAsync(this Browser browser, WebPage page, string formSelector)
    {
        var form = page.Document.QuerySelector<IHtmlFormElement>(formSelector) ?? throw new InvalidOperationException($"No form matches '{formSelector}'.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(page.Url, form.GetAttribute("data-options")));
        request.Headers.Add("RequestVerificationToken", form.QuerySelector<IHtmlInputElement>("input[name=__RequestVerificationToken]")!.Value);

        using var response = await browser.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (json.RootElement.GetProperty("options").Clone(), json.RootElement.GetProperty("state").GetString()!);
    }
}
