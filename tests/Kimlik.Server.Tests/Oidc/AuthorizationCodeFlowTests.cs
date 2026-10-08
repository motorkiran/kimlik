using System.Net;
using System.Net.Http.Headers;
using Kimlik.Server.Tests.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Tests.Oidc;

public sealed class AuthorizationCodeFlowTests(KimlikServerFixture server)
{
    [Fact]
    public async Task SignedOutUser_SignsIn_AndClientReceivesTokens()
    {
        var client = await server.CreateWebClientAsync();
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        var request = new AuthorizationRequest(client.ClientId);
        await server.QueryDatabaseAsync(context => context.Users.Where(candidate => candidate.Id == user.Id).ExecuteUpdateAsync(
            setters => setters.SetProperty(candidate => candidate.PictureUrl, "https://example.com/ada.png").SetProperty(candidate => candidate.TimeZone, "Europe/London"),
            TestContext.Current.CancellationToken));

        using var challenge = await browser.GetAsync(request.Url);
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        challenge.Headers.Location!.AbsolutePath.ShouldBe("/signin");

        var returnUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(challenge.Headers.Location.Query)["ReturnUrl"].ToString();
        using var signIn = await browser.SignInAsync(user.Email, user.Password, returnUrl);
        using var callback = await browser.FollowAsync(signIn);
        var parameters = AuthorizationRequest.ReadCallback(callback);
        parameters["state"].ShouldBe(request.State);

        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, parameters["code"]);

        var identityToken = new JsonWebToken(tokens.GetProperty("id_token").GetString());
        identityToken.Subject.ShouldBe(user.Id.ToString());
        identityToken.GetClaim(Claims.Nonce).Value.ShouldBe(request.Nonce);
        identityToken.GetClaim(Claims.Email).Value.ShouldBe(user.Email);
        identityToken.GetClaim(Claims.EmailVerified).Value.ShouldBe("true");
        identityToken.GetClaim(Claims.Name).Value.ShouldBe("Ada Lovelace");
        identityToken.GetClaim(Claims.Picture).Value.ShouldBe("https://example.com/ada.png");
        identityToken.GetClaim(Claims.Zoneinfo).Value.ShouldBe("Europe/London");
        identityToken.TryGetClaim(Claims.AuthenticationTime, out _).ShouldBeTrue();

        var accessToken = new JsonWebToken(tokens.GetProperty("access_token").GetString());
        accessToken.Subject.ShouldBe(user.Id.ToString());
        accessToken.Audiences.ShouldBe([TestClients.ApiResource]);
        tokens.TryGetProperty("refresh_token", out _).ShouldBeTrue();

        using var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.EncodedToken);
        using var userInfo = await browser.Client.SendAsync(userInfoRequest, TestContext.Current.CancellationToken);
        var claims = await userInfo.ReadJsonAsync();
        claims.GetProperty("sub").GetString().ShouldBe(user.Id.ToString());
        claims.GetProperty("email").GetString().ShouldBe(user.Email);
        claims.GetProperty("zoneinfo").GetString().ShouldBe("Europe/London");
    }

    [Fact]
    public async Task RefreshToken_IsRotated_AndReuseRevokesTheWholeFamily()
    {
        var (browser, client, tokens) = await server.SignInAndRedeemAsync(await server.CreateUserAsync());
        using var _ = browser;
        var firstRefreshToken = tokens.GetProperty("refresh_token").GetString()!;

        using var refreshed = await OidcFlows.RefreshAsync(browser.Client, client, firstRefreshToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondRefreshToken = (await refreshed.ReadJsonAsync()).GetProperty("refresh_token").GetString()!;
        secondRefreshToken.ShouldNotBe(firstRefreshToken);

        // Replaying the first token looks like theft: it is rejected and the tokens issued after it die too.
        using var replayed = await OidcFlows.RefreshAsync(browser.Client, client, firstRefreshToken);
        (await replayed.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe(Errors.InvalidGrant);

        using var afterReplay = await OidcFlows.RefreshAsync(browser.Client, client, secondRefreshToken);
        (await afterReplay.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe(Errors.InvalidGrant);
    }

    [Fact]
    public async Task PromptNone_WithoutSession_ReturnsLoginRequired()
    {
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);

        using var response = await browser.GetAsync(new AuthorizationRequest(client.ClientId) { Prompt = PromptValues.None }.Url);

        AuthorizationRequest.ReadCallback(response)["error"].ShouldBe(Errors.LoginRequired);
    }

    [Fact]
    public async Task ExplicitConsent_IsAskedOnce_ThenRemembered()
    {
        var client = await server.CreateWebClientAsync(ConsentTypes.Explicit);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        var consentPage = await browser.GetPageAsync(new AuthorizationRequest(client.ClientId).Url);
        consentPage.Text.ShouldContain("Orders web app");
        consentPage.Text.ShouldContain("Manage your orders");

        using var accepted = await browser.SubmitAsync(consentPage, submitter: ("consent", "accept"));
        AuthorizationRequest.ReadCallback(accepted).ShouldContainKey("code");

        using var second = await browser.GetAsync(new AuthorizationRequest(client.ClientId).Url);
        AuthorizationRequest.ReadCallback(second).ShouldContainKey("code");
    }

    [Fact]
    public async Task DeniedConsent_ReturnsAccessDenied()
    {
        var client = await server.CreateWebClientAsync(ConsentTypes.Explicit);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        var consentPage = await browser.GetPageAsync(new AuthorizationRequest(client.ClientId).Url);
        using var denied = await browser.SubmitAsync(consentPage, submitter: ("consent", "deny"));

        AuthorizationRequest.ReadCallback(denied)["error"].ShouldBe(Errors.AccessDenied);
    }

    [Fact]
    public async Task UnregisteredRedirectUri_IsShownToTheUser_InsteadOfFollowed()
    {
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);

        using var response = await browser.GetAsync(new AuthorizationRequest(client.ClientId) { RedirectUri = "https://attacker.example/callback" }.Url);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.Location.ShouldBeNull();
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain(Errors.InvalidRequest);
    }

    [Fact]
    public async Task EndSession_WithIdTokenHint_SignsOutAndReturnsToClient()
    {
        var (browser, client, tokens) = await server.SignInAndRedeemAsync(await server.CreateUserAsync());
        using var _ = browser;

        using var signOut = await browser.GetAsync(
            $"/connect/endsession?id_token_hint={tokens.GetProperty("id_token").GetString()}&post_logout_redirect_uri={Uri.EscapeDataString(TestWebClient.PostLogoutRedirectUri)}");

        signOut.Headers.Location!.ToString().ShouldBe(TestWebClient.PostLogoutRedirectUri);

        using var nextAuthorization = await browser.GetAsync(new AuthorizationRequest(client.ClientId).Url);
        nextAuthorization.Headers.Location!.AbsolutePath.ShouldBe("/signin");
    }
}
