using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Kimlik.Server.Tests.Passkeys;

/// <summary>What passkeys.js does in a browser, for the hosted pages' passkey forms.</summary>
internal static class PasskeyForms
{
    /// <summary>
    /// Fetches the options of a passkey form, or of its <paramref name="buttonSelector"/> button, has
    /// <paramref name="answer"/> respond to them as an authenticator would, and posts the answer with the form.
    /// </summary>
    public static async Task<HttpResponseMessage> RunPasskeyFormAsync(
        this Browser browser,
        WebPage page,
        string formSelector,
        Func<JsonElement, string> answer,
        IReadOnlyDictionary<string, string>? values = null,
        string? buttonSelector = null)
    {
        var (options, state) = await browser.FetchPasskeyOptionsAsync(page, formSelector, buttonSelector);
        var fields = new Dictionary<string, string>(values ?? new Dictionary<string, string>()) { ["Credential"] = answer(options), ["State"] = state };
        var action = buttonSelector is null ? null : page.Document.QuerySelector(buttonSelector)!.GetAttribute("formaction");
        return await browser.SubmitAsync(page, fields, formSelector, action: action);
    }

    /// <summary>Signs in with a passkey on the sign-in page, as the "Sign in with a passkey" button does.</summary>
    public static async Task<HttpResponseMessage> SignInWithPasskeyAsync(
        this Browser browser, SoftwareAuthenticator authenticator, string? returnUrl = null, SoftwareAuthenticator.Passkey? passkey = null)
    {
        var page = await browser.GetPageAsync(returnUrl is null ? "/signin" : $"/signin?ReturnUrl={Uri.EscapeDataString(returnUrl)}");
        return await browser.RunPasskeyFormAsync(page, "form", options => authenticator.Get(options, passkey), buttonSelector: "#passkey-sign-in");
    }

    /// <summary>The options and the state that the form's, or the button's, options handler returns.</summary>
    public static async Task<(JsonElement Options, string State)> FetchPasskeyOptionsAsync(
        this Browser browser, WebPage page, string formSelector, string? buttonSelector = null)
    {
        var form = page.Document.QuerySelector<IHtmlFormElement>(formSelector) ?? throw new InvalidOperationException($"No form matches '{formSelector}'.");
        var source = buttonSelector is null ? form : page.Document.QuerySelector(buttonSelector)!;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(page.Url, source.GetAttribute("data-options")));
        request.Headers.Add("RequestVerificationToken", form.QuerySelector<IHtmlInputElement>("input[name=__RequestVerificationToken]")!.Value);

        using var response = await browser.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (json.RootElement.GetProperty("options").Clone(), json.RootElement.GetProperty("state").GetString()!);
    }

    /// <summary>Adds a passkey on the account pages of a browser that signed in within the last ten minutes.</summary>
    public static async Task AddPasskeyAsync(this Browser browser, SoftwareAuthenticator authenticator, string name = "Test passkey")
    {
        var page = await browser.GetPageAsync("/account/passkeys");
        using var added = await browser.RunPasskeyFormAsync(page, "#add-passkey", options => authenticator.Create(options), new Dictionary<string, string> { ["Name"] = name });
        (await Browser.ReadPageAsync(added)).Text.ShouldContain("Your passkey has been added.");
    }
}
