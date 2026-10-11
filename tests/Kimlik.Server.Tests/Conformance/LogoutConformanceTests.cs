using System.Net;
using System.Text;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Conformance;

/// <summary>
/// What the OpenID Foundation's RP-Initiated Logout OP test plan checks (design §8.12): the browser goes back to the
/// client only for a registered address and a valid ID token of the signed-in user; otherwise the user confirms, or sees
/// an error, and nobody is sent anywhere.
/// </summary>
public sealed class LogoutConformanceTests(KimlikServerFixture server)
{
    private const string EndSession = "/connect/endsession";

    /// <summary>oidcc-rp-initiated-logout, oidcc-rp-initiated-logout-no-state.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidHint_SignsOut_AndReturnsToTheClientWithItsState(bool withState)
    {
        var (browser, client, idToken) = await SignInAsync();
        using var _ = browser;
        var parameters = new Dictionary<string, string?> { ["id_token_hint"] = idToken, ["post_logout_redirect_uri"] = TestWebClient.PostLogoutRedirectUri };
        if (withState)
        {
            parameters["state"] = "logout-state";
        }

        using var signedOut = await browser.GetAsync(QueryHelpers.AddQueryString(EndSession, parameters));

        var location = signedOut.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe(TestWebClient.PostLogoutRedirectUri);
        QueryHelpers.ParseQuery(location.Query).TryGetValue("state", out var state).ShouldBe(withState);
        if (withState)
        {
            state.ToString().ShouldBe("logout-state");
        }

        using var silent = await browser.GetAsync(new AuthorizationRequest(client) { Pkce = false, Scope = "openid", Prompt = "none" }.Url);
        AuthorizationRequest.ReadCallback(silent)["error"].ShouldBe("login_required");
    }

    /// <summary>
    /// oidcc-rp-initiated-logout-bad-logout-redirect-uri, -query-added-to-logout-redirect-uri, -modified-id-token-hint,
    /// -bad-id-token-hint, -no-id-token-hint: an error or a confirmation, never a redirect to the client.
    /// </summary>
    [Theory]
    [InlineData("unregistered address")]
    [InlineData("query added")]
    [InlineData("hint changed to alg none")]
    [InlineData("hint signed by someone else")]
    [InlineData("no hint")]
    public async Task DoubtfulRequests_DoNotSendTheBrowserBack(string doubt)
    {
        var (browser, _, idToken) = await SignInAsync();
        using var _ = browser;
        var parameters = new Dictionary<string, string?> { ["id_token_hint"] = idToken, ["post_logout_redirect_uri"] = TestWebClient.PostLogoutRedirectUri, ["state"] = "s" };
        switch (doubt)
        {
            case "unregistered address":
                parameters["post_logout_redirect_uri"] = "https://elsewhere.example.com/signed-out";
                break;
            case "query added":
                parameters["post_logout_redirect_uri"] = TestWebClient.PostLogoutRedirectUri + "?foo=bar";
                break;
            case "hint changed to alg none":
                parameters["id_token_hint"] = $"{Base64UrlEncoder.Encode("""{"alg":"none"}""")}.{idToken.Split('.')[1]}.";
                break;
            case "hint signed by someone else":
                parameters["id_token_hint"] = $"{idToken.Split('.')[0]}.{idToken.Split('.')[1]}.{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes("not Kimlik's signature"))}";
                break;
            default:
                parameters.Remove("id_token_hint");
                break;
        }

        using var response = await browser.GetAsync(QueryHelpers.AddQueryString(EndSession, parameters));

        response.Headers.Location.ShouldBeNull();
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }

    /// <summary>oidcc-rp-initiated-logout-no-params, -only-state: the user confirms, and is signed out.</summary>
    [Fact]
    public async Task RequestWithoutParameters_IsConfirmed_ThenSignsOut()
    {
        var (browser, client, _) = await SignInAsync();
        using var _ = browser;

        var confirmation = await browser.GetPageAsync($"{EndSession}?state=only");
        using var confirmed = await browser.SubmitAsync(confirmation, submitter: ("confirm", "true"));

        confirmed.Headers.Location!.OriginalString.ShouldBe("/");
        using var silent = await browser.GetAsync(new AuthorizationRequest(client) { Pkce = false, Scope = "openid", Prompt = "none" }.Url);
        AuthorizationRequest.ReadCallback(silent)["error"].ShouldBe("login_required");
    }

    /// <summary>A user signed in at a client set up as the conformance suite is, with its ID token.</summary>
    private async Task<(Browser Browser, string ClientId, string IdToken)> SignInAsync()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = $"conformance-{Guid.NewGuid():N}",
            DisplayName = "OpenID conformance suite",
            Type = ClientType.Web,
            RedirectUris = [TestWebClient.RedirectUri],
            PostLogoutRedirectUris = [TestWebClient.PostLogoutRedirectUri],
            Scopes = ["openid"],
            RequirePkce = false,
        });
        var client = await created.ReadAsync<CreatedClientResponse>();
        var user = await server.CreateUserAsync();
        var browser = new Browser(server);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client.Client.ClientId) { Pkce = false, Scope = "openid" };
        using var callback = await browser.GetAsync(request.Url);
        using var tokens = await browser.Client.PostFormAsync(
            "/connect/token",
            [new("grant_type", "authorization_code"), new("code", AuthorizationRequest.ReadCallback(callback)["code"]), new("redirect_uri", request.RedirectUri)],
            new TestClient(client.Client.ClientId, client.ClientSecret!));
        return (browser, client.Client.ClientId, (await tokens.ReadJsonAsync()).GetProperty("id_token").GetString()!);
    }
}
