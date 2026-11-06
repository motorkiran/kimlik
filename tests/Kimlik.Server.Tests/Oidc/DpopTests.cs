using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Kimlik.Application.Clients;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>Tokens bound to a client's key, used with proofs of possession (RFC 9449, design §8.13).</summary>
public sealed class DpopTests(KimlikServerFixture server)
{
    private const string TokenPath = "/connect/token";
    private const string UserInfoPath = "/connect/userinfo";
    private const string OrganizationsPath = "/api/v1/me/organizations";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Discovery_ListsTheProofAlgorithms()
    {
        using var http = server.CreateClient();

        using var response = await http.GetAsync("/.well-known/openid-configuration", CancellationToken);

        (await response.ReadJsonAsync()).GetProperty("dpop_signing_alg_values_supported").EnumerateArray().Select(alg => alg.GetString())
            .ShouldBe(["ES256", "ES384", "PS256", "RS256"]);
    }

    [Fact]
    public async Task ServiceClient_GetsATokenBoundToItsKey_OnlyWithAProof()
    {
        var serviceClient = await server.CreateServiceClientAsync();
        using var key = new DpopKey();
        using var http = server.CreateClient();

        using var bound = await TokenAsync(http, [new("grant_type", "client_credentials")], key.Proof("POST", TokenPath), serviceClient);
        var body = await bound.ReadJsonAsync();
        bound.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        body.GetProperty("token_type").GetString().ShouldBe("DPoP");
        Confirmation(body.GetProperty("access_token").GetString()!).ShouldBe(key.Thumbprint);

        using var bearer = await TokenAsync(http, [new("grant_type", "client_credentials")], proof: null, serviceClient);
        var bearerBody = await bearer.ReadJsonAsync();
        bearerBody.GetProperty("token_type").GetString().ShouldBe("Bearer");
        new JsonWebToken(bearerBody.GetProperty("access_token").GetString()).TryGetPayloadValue<JsonElement>("cnf", out _).ShouldBeFalse();
    }

    public static TheoryData<string> InvalidProofs => new()
    {
        "another method",
        "another URL",
        "too old",
        "from the future",
        "not typed dpop+jwt",
        "symmetric algorithm",
        "private key",
        "another key's signature",
        "no jti",
        "two proofs",
    };

    [Theory]
    [MemberData(nameof(InvalidProofs))]
    public async Task InvalidProof_FailsTheTokenRequest(string flaw)
    {
        var serviceClient = await server.CreateServiceClientAsync();
        using var key = new DpopKey();
        using var other = new DpopKey();
        using var http = server.CreateClient();
        var proof = flaw switch
        {
            "another method" => key.Proof("GET", TokenPath),
            "another URL" => key.Proof("POST", "/connect/introspect"),
            "too old" => key.Proof("POST", TokenPath, issuedAt: DateTimeOffset.UtcNow.AddMinutes(-6)),
            "from the future" => key.Proof("POST", TokenPath, issuedAt: DateTimeOffset.UtcNow.AddMinutes(2)),
            "not typed dpop+jwt" => key.Proof("POST", TokenPath, type: "JWT"),
            "symmetric algorithm" => key.Proof("POST", TokenPath, header: header => header["alg"] = "HS256"),
            "private key" => key.Proof("POST", TokenPath, header: header => header["jwk"]!["d"] = key.PrivateParameter),
            "another key's signature" => WithSignatureOf(key.Proof("POST", TokenPath), other.Proof("POST", TokenPath)),
            "no jti" => key.Proof("POST", TokenPath, payload: payload => payload.Remove("jti")),
            _ => key.Proof("POST", TokenPath),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath) { Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")]) };
        request.Headers.Authorization = Basic(serviceClient);
        request.Headers.Add("DPoP", proof);
        if (flaw == "two proofs")
        {
            request.Headers.Add("DPoP", key.Proof("POST", TokenPath));
        }

        using var response = await http.SendAsync(request, CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_dpop_proof");
    }

    [Fact]
    public async Task Proof_IsAcceptedOnce()
    {
        var serviceClient = await server.CreateServiceClientAsync();
        using var key = new DpopKey();
        using var http = server.CreateClient();
        var proof = key.Proof("POST", TokenPath);

        using var first = await TokenAsync(http, [new("grant_type", "client_credentials")], proof, serviceClient);
        using var again = await TokenAsync(http, [new("grant_type", "client_credentials")], proof, serviceClient);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_dpop_proof");
    }

    [Fact]
    public async Task PublicClient_RefreshesOnlyWithTheSameKey()
    {
        var user = await server.CreateUserAsync();
        var client = await server.CreateWebClientAsync();
        var authorization = new AuthorizationRequest(client.ClientId);
        var (browser, code) = await server.SignInForCodeAsync(user, authorization);
        using var _ = browser;
        using var key = new DpopKey();
        using var other = new DpopKey();
        using var http = server.CreateClient();

        using var redeemed = await TokenAsync(http, CodeRedemption(client, authorization, code), key.Proof("POST", TokenPath));
        var tokens = await redeemed.ReadJsonAsync();
        redeemed.StatusCode.ShouldBe(HttpStatusCode.OK, tokens.ToString());
        tokens.GetProperty("token_type").GetString().ShouldBe("DPoP");
        var refreshToken = tokens.GetProperty("refresh_token").GetString()!;

        using var withoutProof = await TokenAsync(http, Refresh(client, refreshToken), proof: null);
        (await withoutProof.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_dpop_proof");
        using var withAnotherKey = await TokenAsync(http, Refresh(client, refreshToken), other.Proof("POST", TokenPath));
        (await withAnotherKey.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_dpop_proof");

        using var refreshed = await TokenAsync(http, Refresh(client, refreshToken), key.Proof("POST", TokenPath));
        var refreshedTokens = await refreshed.ReadJsonAsync();
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, refreshedTokens.ToString());
        Confirmation(refreshedTokens.GetProperty("access_token").GetString()!).ShouldBe(key.Thumbprint);
    }

    [Fact]
    public async Task CodeBoundToAKey_IsRedeemedOnlyWithAProofByIt()
    {
        var user = await server.CreateUserAsync();
        var client = await server.CreateWebClientAsync();
        using var key = new DpopKey();
        using var other = new DpopKey();
        var authorization = new AuthorizationRequest(client.ClientId) { Extra = new Dictionary<string, string> { ["dpop_jkt"] = key.Thumbprint } };
        var (browser, code) = await server.SignInForCodeAsync(user, authorization);
        using var _ = browser;
        using var http = server.CreateClient();

        using var withAnotherKey = await TokenAsync(http, CodeRedemption(client, authorization, code), other.Proof("POST", TokenPath));
        (await withAnotherKey.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_dpop_proof");

        using var redeemed = await TokenAsync(http, CodeRedemption(client, authorization, code), key.Proof("POST", TokenPath));
        var tokens = await redeemed.ReadJsonAsync();
        redeemed.StatusCode.ShouldBe(HttpStatusCode.OK, tokens.ToString());
        Confirmation(tokens.GetProperty("access_token").GetString()!).ShouldBe(key.Thumbprint);
    }

    [Fact]
    public async Task AuthorizationRequest_WithAMalformedThumbprint_IsRefused()
    {
        var user = await server.CreateUserAsync();
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var authorization = new AuthorizationRequest(client.ClientId) { Extra = new Dictionary<string, string> { ["dpop_jkt"] = "not-a-thumbprint" } };

        using var callback = await browser.GetAsync(authorization.Url);

        AuthorizationRequest.ReadCallback(callback)["error"].ShouldBe("invalid_request");
    }

    [Fact]
    public async Task UserInfo_TakesABoundToken_OnlyWithAProofForIt()
    {
        var (key, accessToken, _) = await SignInWithKeyAsync("openid profile");
        using var http = server.CreateClient();

        using var withProof = await SendAsync(http, HttpMethod.Get, UserInfoPath, "DPoP", accessToken, key.Proof("GET", UserInfoPath, accessToken));
        withProof.StatusCode.ShouldBe(HttpStatusCode.OK, await withProof.Content.ReadAsStringAsync(CancellationToken));

        using var asBearer = await SendAsync(http, HttpMethod.Get, UserInfoPath, "Bearer", accessToken, proof: null);
        asBearer.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var withoutHash = await SendAsync(http, HttpMethod.Get, UserInfoPath, "DPoP", accessToken, key.Proof("GET", UserInfoPath));
        withoutHash.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        withoutHash.Headers.WwwAuthenticate.ToString().ShouldStartWith("DPoP algs=");
        withoutHash.Headers.WwwAuthenticate.ToString().ShouldContain("error=\"invalid_dpop_proof\"");

        using var other = new DpopKey();
        using var byAnotherKey = await SendAsync(http, HttpMethod.Get, UserInfoPath, "DPoP", accessToken, other.Proof("GET", UserInfoPath, accessToken));
        byAnotherKey.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task KimliksApi_TakesABoundToken_OnlyWithAFreshProofForIt()
    {
        var (key, accessToken, _) = await SignInWithKeyAsync($"openid {KimlikScopes.Organizations}");
        using var http = server.CreateClient();
        var proof = key.Proof("GET", OrganizationsPath, accessToken);

        using var withProof = await SendAsync(http, HttpMethod.Get, OrganizationsPath, "DPoP", accessToken, proof);
        withProof.StatusCode.ShouldBe(HttpStatusCode.OK, await withProof.Content.ReadAsStringAsync(CancellationToken));

        using var replayed = await SendAsync(http, HttpMethod.Get, OrganizationsPath, "DPoP", accessToken, proof);
        replayed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        replayed.Headers.WwwAuthenticate.ToString().ShouldContain("error=\"invalid_dpop_proof\"");

        using var asBearer = await SendAsync(http, HttpMethod.Get, OrganizationsPath, "Bearer", accessToken, proof: null);
        asBearer.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var forAnotherRequest = await SendAsync(http, HttpMethod.Get, OrganizationsPath, "DPoP", accessToken, key.Proof("GET", "/api/v1/me/invitations", accessToken));
        forAnotherRequest.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnboundToken_IsNotTakenAsADpopToken()
    {
        var user = await server.CreateUserAsync();
        var (browser, _, tokens) = await server.SignInAndRedeemAsync(user, $"openid {KimlikScopes.Organizations}");
        using var _ = browser;
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        using var key = new DpopKey();
        using var http = server.CreateClient();

        using var response = await SendAsync(http, HttpMethod.Get, OrganizationsPath, "DPoP", accessToken, key.Proof("GET", OrganizationsPath, accessToken));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Introspection_SaysWhichKeyTheTokenIsBoundTo()
    {
        var serviceClient = await server.CreateServiceClientAsync();
        using var key = new DpopKey();
        using var http = server.CreateClient();
        using var issued = await TokenAsync(http, [new("grant_type", "client_credentials")], key.Proof("POST", TokenPath), serviceClient);
        var accessToken = (await issued.ReadJsonAsync()).GetProperty("access_token").GetString()!;

        using var introspection = await http.PostFormAsync("/connect/introspect", [new("token", accessToken)], serviceClient);

        var body = await introspection.ReadJsonAsync();
        body.GetProperty("active").GetBoolean().ShouldBeTrue();
        body.GetProperty("cnf").GetProperty("jkt").GetString().ShouldBe(key.Thumbprint);
    }

    [Fact]
    public async Task ClientThatRequiresDpop_GetsNoTokenWithoutAProof()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = $"worker-{Guid.NewGuid():N}",
            DisplayName = "Worker",
            Type = ClientType.Service,
            Scopes = [TestClients.ApiScope],
            RequireDpop = true,
        });
        var client = await created.ReadAsync<CreatedClientResponse>();
        client.Client.RequireDpop.ShouldBeTrue();
        var serviceClient = new TestClient(client.Client.ClientId, client.ClientSecret!);
        using var key = new DpopKey();
        using var http = server.CreateClient();

        using var withoutProof = await TokenAsync(http, [new("grant_type", "client_credentials"), new("scope", TestClients.ApiScope)], proof: null, serviceClient);
        (await withoutProof.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_dpop_proof");

        using var withProof = await TokenAsync(http, [new("grant_type", "client_credentials"), new("scope", TestClients.ApiScope)], key.Proof("POST", TokenPath), serviceClient);
        (await withProof.ReadJsonAsync()).GetProperty("token_type").GetString().ShouldBe("DPoP");

        using var relaxed = await api.Http.SendJsonAsync(HttpMethod.Patch, $"/api/v1/clients/{client.Client.Id}", """{ "requireDpop": false }""");
        (await relaxed.ReadAsync<ClientResponse>()).RequireDpop.ShouldBeFalse();
        (await server.WithServicesAsync(async services =>
        {
            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            var application = await applications.FindByClientIdAsync(serviceClient.ClientId, CancellationToken);
            return ClientPresets.RequiresDpop(await applications.GetPropertiesAsync(application!, CancellationToken));
        })).ShouldBeFalse();
    }

    /// <summary>Signs a new user in to a new browser app, which redeems the code with a proof by a new key.</summary>
    private async Task<(DpopKey Key, string AccessToken, JsonElement Tokens)> SignInWithKeyAsync(string scope)
    {
        var user = await server.CreateUserAsync();
        var client = await server.CreateWebClientAsync();
        var authorization = new AuthorizationRequest(client.ClientId) { Scope = scope };
        var (browser, code) = await server.SignInForCodeAsync(user, authorization);
        browser.Dispose();
        var key = new DpopKey();
        using var http = server.CreateClient();

        using var redeemed = await TokenAsync(http, CodeRedemption(client, authorization, code), key.Proof("POST", TokenPath));
        var tokens = await redeemed.ReadJsonAsync();
        redeemed.StatusCode.ShouldBe(HttpStatusCode.OK, tokens.ToString());
        return (key, tokens.GetProperty("access_token").GetString()!, tokens);
    }

    private static async Task<HttpResponseMessage> TokenAsync(
        HttpClient http, IEnumerable<KeyValuePair<string, string>> fields, string? proof, TestClient? authenticatedAs = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath) { Content = new FormUrlEncodedContent(fields) };
        if (authenticatedAs is not null)
        {
            request.Headers.Authorization = Basic(authenticatedAs);
        }

        if (proof is not null)
        {
            request.Headers.Add("DPoP", proof);
        }

        return await http.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path, string scheme, string accessToken, string? proof)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, accessToken);
        if (proof is not null)
        {
            request.Headers.Add("DPoP", proof);
        }

        return await http.SendAsync(request, CancellationToken);
    }

    private static KeyValuePair<string, string>[] CodeRedemption(TestWebClient client, AuthorizationRequest authorization, string code) =>
    [
        new("grant_type", "authorization_code"),
        new("client_id", client.ClientId),
        new("code", code),
        new("code_verifier", authorization.CodeVerifier),
        new("redirect_uri", authorization.RedirectUri),
    ];

    private static KeyValuePair<string, string>[] Refresh(TestWebClient client, string refreshToken) =>
        [new("grant_type", "refresh_token"), new("client_id", client.ClientId), new("refresh_token", refreshToken)];

    private static AuthenticationHeaderValue Basic(TestClient client) =>
        new("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(client.ClientId)}:{Uri.EscapeDataString(client.ClientSecret)}")));

    private static string? Confirmation(string accessToken) =>
        new JsonWebToken(accessToken).TryGetPayloadValue<JsonElement>("cnf", out var confirmation) ? confirmation.GetProperty("jkt").GetString() : null;

    /// <summary>The header and payload of one proof with the signature of another.</summary>
    private static string WithSignatureOf(string proof, string other) => string.Join('.', proof.Split('.')[..2].Append(other.Split('.')[2]));
}
