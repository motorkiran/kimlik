using System.Net;
using Kimlik.Infrastructure.Security.TokenKeys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kimlik.Server.Tests.Oidc;

public sealed class ClientCredentialsTests(KimlikServerFixture server)
{
    [Fact]
    public async Task Token_IsJwtSignedWithActiveKey_ForTheClientAndRequestedApi()
    {
        var serviceClient = await server.CreateServiceClientAsync();
        using var client = server.CreateClient();

        var token = new JsonWebToken(await client.RequestClientCredentialsTokenAsync(serviceClient));

        token.Typ.ShouldBe("at+jwt");
        token.Kid.ShouldBe(server.Services.GetRequiredService<TokenKeyRing>().Current!.ActiveKey(TokenKeyUse.Signing).KeyId);
        token.Issuer.ShouldBe(TestConfiguration.PublicUrl);
        token.Subject.ShouldBe(serviceClient.ClientId);
        token.Audiences.ShouldBe([TestClients.ApiResource]);
        token.GetClaim("scope").Value.ShouldBe(TestClients.ApiScope);
    }

    [Fact]
    public async Task Token_IsRejected_ForWrongClientSecret()
    {
        var serviceClient = await server.CreateServiceClientAsync();
        using var client = server.CreateClient();

        using var response = await client.PostFormAsync(
            "/connect/token",
            [new("grant_type", "client_credentials")],
            serviceClient with { ClientSecret = "wrong-secret" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_client");
    }

    [Fact]
    public async Task RevokedToken_IsNoLongerActive()
    {
        var serviceClient = await server.CreateServiceClientAsync();
        using var client = server.CreateClient();
        var token = await client.RequestClientCredentialsTokenAsync(serviceClient);

        (await IntrospectAsync(client, serviceClient, token)).ShouldBeTrue();

        using var revocation = await client.PostFormAsync("/connect/revoke", [new("token", token)], serviceClient);
        revocation.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await IntrospectAsync(client, serviceClient, token)).ShouldBeFalse();
    }

    private static async Task<bool> IntrospectAsync(HttpClient client, TestClient serviceClient, string token)
    {
        using var response = await client.PostFormAsync("/connect/introspect", [new("token", token)], serviceClient);
        response.EnsureSuccessStatusCode();
        return (await response.ReadJsonAsync()).GetProperty("active").GetBoolean();
    }
}
