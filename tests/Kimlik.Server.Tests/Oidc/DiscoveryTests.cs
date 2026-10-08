using System.Net;
using Kimlik.Infrastructure.Security.TokenKeys;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.Oidc;

public sealed class DiscoveryTests(KimlikServerFixture server)
{
    [Fact]
    public async Task Configuration_DescribesTheServer()
    {
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        var document = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        document.GetProperty("issuer").GetString().ShouldBe(TestConfiguration.PublicUrl);
        document.GetProperty("token_endpoint").GetString().ShouldBe($"{TestConfiguration.PublicUrl}connect/token");
        document.GetProperty("jwks_uri").GetString().ShouldBe($"{TestConfiguration.PublicUrl}.well-known/jwks");
        document.GetProperty("grant_types_supported").EnumerateArray().Select(value => value.GetString()).ShouldContain("client_credentials");
    }

    [Fact]
    public async Task Jwks_PublishesPublicPartOfEverySigningKey()
    {
        using var client = server.CreateClient();
        var signingKeyIds = await server.QueryDatabaseAsync(context => context.TokenKeys
            .Where(key => key.Use == TokenKeyUse.Signing)
            .Select(key => key.KeyId)
            .ToListAsync(TestContext.Current.CancellationToken));

        using var response = await client.GetAsync("/.well-known/jwks", TestContext.Current.CancellationToken);
        var keys = (await response.ReadJsonAsync()).GetProperty("keys").EnumerateArray().ToList();

        keys.Select(key => key.GetProperty("kid").GetString()).ShouldBe(signingKeyIds, ignoreOrder: true);
        keys.ShouldAllBe(key => key.GetProperty("kty").GetString() == "RSA");
        keys.Where(key => key.TryGetProperty("d", out _)).ShouldBeEmpty("JWKS must never contain private key parameters.");
    }
}
