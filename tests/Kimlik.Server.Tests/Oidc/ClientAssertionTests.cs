using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Api;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>Clients that authenticate with keys instead of a secret (<c>private_key_jwt</c>, RFC 7523).</summary>
public sealed class ClientAssertionTests(KimlikServerFixture server)
{
    private const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    [Fact]
    public async Task ServiceClient_WithKeys_SignsItsWayIn_UntilANewSecretReplacesThem()
    {
        using var key = RSA.Create(2048);
        using var api = await server.CreateApiClientAsync();
        var clientId = $"worker-{Guid.NewGuid():N}"[..24];
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = clientId,
            DisplayName = "Worker",
            Type = ClientType.Service,
            Scopes = [TestClients.ApiScope],
            JsonWebKeySet = TestKeys.KeySet(key, "key-1"),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var client = await created.ReadAsync<CreatedClientResponse>();
        client.ClientSecret.ShouldBeNull();
        client.Client.JsonWebKeySet!["keys"]![0]!["kid"]!.GetValue<string>().ShouldBe("key-1");

        using var http = server.CreateClient();
        var issuer = (await DiscoverAsync(http)).GetProperty("issuer").GetString()!;
        using var granted = await RequestTokenAsync(http, clientId, Assertion(key, clientId, issuer));
        (await granted.ReadJsonAsync()).GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();

        using var otherKey = RSA.Create(2048);
        using var forged = await RequestTokenAsync(http, clientId, Assertion(otherKey, clientId, issuer));
        (await forged.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_client");

        using var regenerated = await api.Http.PostAsync($"/api/v1/clients/{client.Client.Id}/secret");
        var secret = (await regenerated.ReadAsync<ClientSecretResponse>()).ClientSecret;
        using var withKeys = await RequestTokenAsync(http, clientId, Assertion(key, clientId, issuer));
        (await withKeys.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_client");
        (await http.RequestClientCredentialsTokenAsync(new TestClient(clientId, secret))).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Keys_ReplaceTheSecret_OfAnExistingClient()
    {
        var serviceClient = await server.CreateServiceClientAsync();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var api = await server.CreateApiClientAsync();
        using var http = server.CreateClient();
        var id = await server.QueryDatabaseAsync(context => Task.FromResult(context.Applications.Single(application => application.ClientId == serviceClient.ClientId).Id));

        using var updated = await api.Http.SendJsonAsync(
            HttpMethod.Patch, $"/api/v1/clients/{id}", JsonSerializer.Serialize(new { jsonWebKeySet = TestKeys.KeySet(key, "ec-1") }));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var issuer = (await DiscoverAsync(http)).GetProperty("issuer").GetString()!;
        using var granted = await RequestTokenAsync(http, serviceClient.ClientId, Assertion(key, serviceClient.ClientId, issuer));
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var withSecret = await http.PostFormAsync("/connect/token",
        [
            new("grant_type", "client_credentials"),
            new("client_id", serviceClient.ClientId),
            new("client_secret", serviceClient.ClientSecret),
        ]);
        (await withSecret.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_client");

        using var cleared = await api.Http.SendJsonAsync(HttpMethod.Patch, $"/api/v1/clients/{id}", """{ "jsonWebKeySet": null }""");
        (await cleared.ReadProblemCodeAsync()).ShouldBe("client.keys_required");
    }

    [Theory]
    [InlineData(ClientType.Service, "private", "client.invalid_keys")]
    [InlineData(ClientType.Service, "short", "client.invalid_keys")]
    [InlineData(ClientType.Spa, "public", "client.keys_not_supported")]
    public async Task UnsuitableKeys_AreRefused(ClientType type, string keys, string error)
    {
        using var rsa = RSA.Create(keys == "short" ? 1024 : 2048);
        var set = TestKeys.KeySet(rsa, "key-1", includePrivateParameters: keys == "private");
        using var api = await server.CreateApiClientAsync();

        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = $"keys-{Guid.NewGuid():N}"[..24],
            DisplayName = "Keys",
            Type = type,
            RedirectUris = type == ClientType.Spa ? [TestWebClient.RedirectUri] : [],
            JsonWebKeySet = set,
        });

        (await created.ReadProblemCodeAsync()).ShouldBe(error);
    }

    [Fact]
    public async Task Discovery_AdvertisesKeysAsAWayToAuthenticate()
    {
        using var http = server.CreateClient();

        var configuration = await DiscoverAsync(http);

        configuration.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(method => method.GetString())
            .ShouldContain("private_key_jwt");
    }

    private static async Task<JsonElement> DiscoverAsync(HttpClient http)
    {
        using var response = await http.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        return await response.ReadJsonAsync();
    }

    private static Task<HttpResponseMessage> RequestTokenAsync(HttpClient http, string clientId, string assertion) =>
        http.PostFormAsync("/connect/token",
        [
            new("grant_type", "client_credentials"),
            new("client_id", clientId),
            new("client_assertion_type", AssertionType),
            new("client_assertion", assertion),
            new("scope", TestClients.ApiScope),
        ]);

    /// <summary>A client assertion: a short-lived JWT the client signs about itself, for Kimlik.</summary>
    private static string Assertion(AsymmetricAlgorithm key, string clientId, string issuer) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        // Explicit typing (draft-ietf-oauth-rfc7523bis) keeps other JWTs from passing as assertions.
        TokenType = "client-authentication+jwt",
        Issuer = clientId,
        Audience = issuer,
        Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = clientId, [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N") },
        Expires = DateTime.UtcNow.AddMinutes(2),
        SigningCredentials = key switch
        {
            RSA rsa => new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256),
            ECDsa ecdsa => new SigningCredentials(new ECDsaSecurityKey(ecdsa), SecurityAlgorithms.EcdsaSha256),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        },
    });
}
