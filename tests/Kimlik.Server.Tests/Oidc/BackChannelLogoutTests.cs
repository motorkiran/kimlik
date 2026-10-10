using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>Web apps hear, server to server, when the sessions they signed users in through end (OIDC Back-Channel Logout 1.0).</summary>
public sealed class BackChannelLogoutTests(KimlikServerFixture server)
{
    private const string LogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    [Fact]
    public async Task SigningOut_SendsTheSessionsAppsASignedLogoutToken()
    {
        var endpoint = server.Webhooks.NewEndpoint();
        var app = await CreateWebAppAsync(endpoint);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var sessionId = Payload((await SignInToAsync(browser, app)).GetProperty("id_token").GetString()!).GetProperty("sid").GetString();
        sessionId.ShouldNotBeNullOrEmpty();

        using var signedOut = await browser.SubmitAsync(await browser.GetPageAsync("/signout"));

        var logoutToken = await LogoutTokenAsync(endpoint);
        var validation = await ValidateAsync(logoutToken, app.ClientId);
        validation.IsValid.ShouldBeTrue(validation.Exception?.Message);
        var token = (JsonWebToken)validation.SecurityToken;
        token.Typ.ShouldBe("logout+jwt");
        token.Subject.ShouldBe(user.Id.ToString());
        token.GetClaim("sid").Value.ShouldBe(sessionId);
        JsonDocument.Parse(token.GetClaim("events").Value).RootElement.TryGetProperty(LogoutEvent, out _).ShouldBeTrue();
        token.TryGetPayloadValue<string>("nonce", out _).ShouldBeFalse();
        (await server.QueryDatabaseAsync(context => context.SessionClients.AnyAsync(record => record.SessionId == sessionId))).ShouldBeFalse();
    }

    [Fact]
    public async Task SigningOutEverywhere_TellsTheAppsOfEverySession()
    {
        var endpoint = server.Webhooks.NewEndpoint();
        var app = await CreateWebAppAsync(endpoint);
        var user = await server.CreateUserAsync();
        using (var browser = new Browser(server))
        {
            using var signIn = await browser.SignInAsync(user.Email, user.Password);
            await SignInToAsync(browser, app);
        }

        using var api = await server.CreateApiClientAsync();
        using var revoked = await api.Http.DeleteAsync($"/api/v1/users/{user.Id}/sessions", TestContext.Current.CancellationToken);
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var token = new JsonWebToken(await LogoutTokenAsync(endpoint));
        token.Subject.ShouldBe(user.Id.ToString());
        token.TryGetPayloadValue<string>("sid", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(ClientType.Spa, "https://app.example.com/logout", "client.back_channel_logout_not_supported")]
    [InlineData(ClientType.Web, "http://app.example.com/logout", "client.invalid_back_channel_logout_uri")]
    public async Task UnsuitableEndpoints_AreRefused(ClientType type, string uri, string error)
    {
        using var api = await server.CreateApiClientAsync();

        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = $"logout-{Guid.NewGuid():N}"[..24],
            DisplayName = "Logout",
            Type = type,
            RedirectUris = [TestWebClient.RedirectUri],
            BackChannelLogoutUri = uri,
        });

        (await created.ReadProblemCodeAsync()).ShouldBe(error);
    }

    [Fact]
    public async Task Discovery_AdvertisesSessionLogout()
    {
        using var http = server.CreateClient();

        using var response = await http.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        var configuration = await response.ReadJsonAsync();

        configuration.GetProperty("backchannel_logout_supported").GetBoolean().ShouldBeTrue();
        configuration.GetProperty("backchannel_logout_session_supported").GetBoolean().ShouldBeTrue();
    }

    private async Task<TestClient> CreateWebAppAsync(string backChannelLogoutUri)
    {
        using var api = await server.CreateApiClientAsync();
        var clientId = $"web-{Guid.NewGuid():N}"[..24];
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = clientId,
            DisplayName = "Orders web app",
            Type = ClientType.Web,
            RedirectUris = [TestWebClient.RedirectUri],
            Scopes = ["openid"],
            BackChannelLogoutUri = backChannelLogoutUri,
        });
        var client = await created.ReadAsync<CreatedClientResponse>();
        client.Client.BackChannelLogoutUri.ShouldBe(backChannelLogoutUri);
        return new TestClient(clientId, client.ClientSecret!);
    }

    /// <summary>Signs the browser's user in to the web app, which redeems the code with its secret.</summary>
    private static async Task<JsonElement> SignInToAsync(Browser browser, TestClient app)
    {
        var request = new AuthorizationRequest(app.ClientId) { Scope = "openid" };
        using var callback = await browser.GetAsync(request.Url);
        using var redeemed = await browser.Client.PostFormAsync(
            "/connect/token",
            [
                new("grant_type", "authorization_code"),
                new("code", AuthorizationRequest.ReadCallback(callback)["code"]),
                new("code_verifier", request.CodeVerifier),
                new("redirect_uri", request.RedirectUri),
            ],
            app);
        var tokens = await redeemed.ReadJsonAsync();
        redeemed.StatusCode.ShouldBe(HttpStatusCode.OK, tokens.ToString());
        return tokens;
    }

    private async Task<string> LogoutTokenAsync(string endpoint)
    {
        var received = (await server.Webhooks.WaitForAsync(endpoint, _ => true)).ShouldHaveSingleItem();
        return QueryHelpers.ParseQuery(received.Body)["logout_token"].ToString();
    }

    private async Task<TokenValidationResult> ValidateAsync(string logoutToken, string clientId)
    {
        using var http = server.CreateClient();
        using var keys = await http.GetAsync("/.well-known/jwks", TestContext.Current.CancellationToken);
        var keySet = new JsonWebKeySet(await keys.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await new JsonWebTokenHandler().ValidateTokenAsync(logoutToken, new TokenValidationParameters
        {
            ValidIssuer = TestConfiguration.PublicUrl,
            ValidAudience = clientId,
            IssuerSigningKeys = keySet.GetSigningKeys(),
            ValidTypes = ["logout+jwt"],
        });
    }

    private static JsonElement Payload(string jwt) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]))).RootElement.Clone();
}
