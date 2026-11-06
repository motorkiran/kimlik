using System.Net;
using System.Text.Json;
using Kimlik.Server.Tests.Accounts;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>Complete protocol flows shared by tests that need a signed-in user with tokens.</summary>
internal static class OidcFlows
{
    /// <summary>Signs the user in through the hosted pages and redeems an authorization code for a new web client.</summary>
    public static async Task<(Browser Browser, TestWebClient Client, JsonElement Tokens)> SignInAndRedeemAsync(
        this KimlikServerFixture server, TestUser user, string? scope = null)
    {
        var client = await server.CreateWebClientAsync();
        var request = scope is null ? new AuthorizationRequest(client.ClientId) : new AuthorizationRequest(client.ClientId) { Scope = scope };
        var (browser, code) = await server.SignInForCodeAsync(user, request);
        var tokens = await RedeemCodeAsync(browser.Client, client, request, code);

        return (browser, client, tokens);
    }

    /// <summary>Signs the user in through the hosted pages and returns the authorization code of <paramref name="request"/>.</summary>
    public static async Task<(Browser Browser, string Code)> SignInForCodeAsync(this KimlikServerFixture server, TestUser user, AuthorizationRequest request)
    {
        var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var callback = await browser.GetAsync(request.Url);

        // Access to Kimlik's own API takes the user's consent, even for first-party apps.
        if (callback.StatusCode == HttpStatusCode.OK)
        {
            var consent = await Browser.ReadPageAsync(callback);
            callback.Dispose();
            callback = await browser.SubmitAsync(consent, submitter: ("consent", "accept"));
        }

        using var _ = callback;
        return (browser, AuthorizationRequest.ReadCallback(callback)["code"]);
    }

    public static async Task<JsonElement> RedeemCodeAsync(HttpClient httpClient, TestWebClient client, AuthorizationRequest request, string code)
    {
        using var response = await httpClient.PostFormAsync("/connect/token",
        [
            new("grant_type", "authorization_code"),
            new("client_id", client.ClientId),
            new("code", code),
            new("code_verifier", request.CodeVerifier),
            new("redirect_uri", request.RedirectUri),
        ]);

        var body = await response.ReadJsonAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        return body;
    }

    public static Task<HttpResponseMessage> RefreshAsync(HttpClient httpClient, TestWebClient client, string refreshToken) =>
        httpClient.PostFormAsync("/connect/token",
        [
            new("grant_type", "refresh_token"),
            new("client_id", client.ClientId),
            new("refresh_token", refreshToken),
        ]);
}
