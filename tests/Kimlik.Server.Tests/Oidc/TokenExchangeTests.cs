using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts.Management;
using Kimlik.Server.Oidc;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>A backend exchanges a user's access token for one to another API, to call it on the user's behalf (RFC 8693).</summary>
public sealed class TokenExchangeTests(KimlikServerFixture server)
{
    private const string Grant = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    [Fact]
    public async Task Backend_GetsATokenForTheOtherApi_ActingForTheUser()
    {
        var (user, orders, billing, subjectToken) = await SetUpAsync();
        var gateway = await CreateServiceAsync(orders.Audience, billing.Scope, allowTokenExchange: true);
        using var http = server.CreateClient();

        using var exchanged = await ExchangeAsync(http, gateway, subjectToken, billing.Scope);

        var body = await exchanged.ReadJsonAsync();
        exchanged.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        body.GetProperty("issued_token_type").GetString().ShouldBe(AccessTokenType);
        body.TryGetProperty("refresh_token", out _).ShouldBeFalse();
        body.TryGetProperty("id_token", out _).ShouldBeFalse();
        var token = Payload(body.GetProperty("access_token").GetString()!);
        token.GetProperty("sub").GetString().ShouldBe(user.Id.ToString());
        token.GetProperty("client_id").GetString().ShouldBe(gateway.ClientId);
        Audiences(token).ShouldContain(billing.Audience);
        token.GetProperty("act").GetProperty("sub").GetString().ShouldBe(gateway.ClientId);
        token.GetProperty("act").GetProperty("client_id").GetString().ShouldBe(gateway.ClientId);
        token.GetProperty("amr").EnumerateArray().Select(method => method.GetString()).ShouldBe(["pwd"]);
    }

    [Fact]
    public async Task TokenForAnotherApi_IsNotExchanged()
    {
        var (_, _, billing, subjectToken) = await SetUpAsync();
        var stranger = await CreateServiceAsync($"stranger-{Guid.NewGuid():N}"[..24], billing.Scope, allowTokenExchange: true);
        using var http = server.CreateClient();

        using var refused = await ExchangeAsync(http, stranger, subjectToken, billing.Scope);

        (await refused.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task ClientWithoutThePermission_IsRefused()
    {
        var (_, orders, billing, subjectToken) = await SetUpAsync();
        var gateway = await CreateServiceAsync(orders.Audience, billing.Scope, allowTokenExchange: false);
        using var http = server.CreateClient();

        using var refused = await ExchangeAsync(http, gateway, subjectToken, billing.Scope);

        (await refused.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("unauthorized_client");
    }

    [Fact]
    public async Task UserScopes_AndServiceTokens_AreNotExchanged()
    {
        var (_, orders, billing, subjectToken) = await SetUpAsync();
        var gateway = await CreateServiceAsync(orders.Audience, billing.Scope, allowTokenExchange: true);
        using var http = server.CreateClient();

        using var withOpenId = await ExchangeAsync(http, gateway, subjectToken, $"openid {billing.Scope}");
        (await withOpenId.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_scope");

        var serviceToken = await http.RequestClientCredentialsTokenAsync(await server.CreateServiceClientAsync());
        using var ofAService = await ExchangeAsync(http, gateway, serviceToken, billing.Scope);
        (await ofAService.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public void ExchangedImpersonation_StillNamesTheAdministrator()
    {
        var administrator = Guid.NewGuid().ToString();
        var impersonation = new ClaimsIdentity("test");
        OidcPrincipalFactory.AddActor(impersonation, administrator, TimeSpan.FromMinutes(30));
        var exchanged = new ClaimsIdentity("test");

        OidcPrincipalFactory.AddDelegation(exchanged, "gateway", new ClaimsPrincipal(impersonation));

        var act = JsonDocument.Parse(exchanged.FindFirst("act")!.Value).RootElement;
        act.GetProperty("client_id").GetString().ShouldBe("gateway");
        act.GetProperty("act").GetProperty("sub").GetString().ShouldBe(administrator);
        OidcPrincipalFactory.GetActor(new ClaimsPrincipal(exchanged)).ShouldBe(administrator);
        OidcPrincipalFactory.HasActor(new ClaimsPrincipal(exchanged)).ShouldBeTrue();
    }

    private async Task<(string Scope, string Audience)> CreateApiAsync()
    {
        using var api = await server.CreateApiClientAsync();
        var scope = $"billing-{Guid.NewGuid():N}"[..20];
        using var created = await api.Http.PostJsonAsync("/api/v1/api-resources", new CreateApiResourceRequest { Scope = scope, Audience = $"{scope}-api" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (scope, $"{scope}-api");
    }

    /// <summary>
    /// Two APIs, orders and billing, and a user signed in to an app that calls orders: the access token orders receives is
    /// the one to exchange.
    /// </summary>
    private async Task<(TestUser User, (string Scope, string Audience) Orders, (string Scope, string Audience) Billing, string SubjectToken)> SetUpAsync()
    {
        var orders = await CreateApiAsync();
        var billing = await CreateApiAsync();
        using var api = await server.CreateApiClientAsync();
        var appId = $"app-{Guid.NewGuid():N}"[..24];
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = appId,
            DisplayName = "Orders web app",
            Type = ClientType.Spa,
            RedirectUris = [TestWebClient.RedirectUri],
            Scopes = ["openid", orders.Scope],
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(appId) { Scope = $"openid {orders.Scope}" };
        using var callback = await browser.GetAsync(request.Url);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, new TestWebClient(appId), request, AuthorizationRequest.ReadCallback(callback)["code"]);
        return (user, orders, billing, tokens.GetProperty("access_token").GetString()!);
    }

    /// <summary>A service client; an API registers as one whose client ID is its audience.</summary>
    private async Task<TestClient> CreateServiceAsync(string clientId, string scope, bool allowTokenExchange)
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = clientId,
            DisplayName = "Gateway",
            Type = ClientType.Service,
            Scopes = [scope],
            AllowTokenExchange = allowTokenExchange,
        });
        var client = await created.ReadAsync<CreatedClientResponse>();
        client.Client.AllowTokenExchange.ShouldBe(allowTokenExchange);
        return new TestClient(clientId, client.ClientSecret!);
    }

    private static Task<HttpResponseMessage> ExchangeAsync(HttpClient http, TestClient client, string subjectToken, string scope) =>
        http.PostFormAsync(
            "/connect/token",
            [
                new("grant_type", Grant),
                new("subject_token", subjectToken),
                new("subject_token_type", AccessTokenType),
                new("scope", scope),
            ],
            client);

    private static string[] Audiences(JsonElement token) => token.GetProperty("aud") switch
    {
        { ValueKind: JsonValueKind.Array } audiences => [.. audiences.EnumerateArray().Select(audience => audience.GetString()!)],
        var audience => [audience.GetString()!],
    };

    private static JsonElement Payload(string jwt) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]))).RootElement.Clone();
}
