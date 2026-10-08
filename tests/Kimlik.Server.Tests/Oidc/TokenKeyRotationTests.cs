using Kimlik.Infrastructure.Security.TokenKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>Changes the shared key ring, so it runs on its own instead of alongside tests that read JWKS.</summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class KeyRingIsolation;

[Collection(typeof(KeyRingIsolation))]
public sealed class TokenKeyRotationTests(KimlikServerFixture server)
{
    [Fact]
    public async Task PendingKey_IsPublishedBeforeActivation_WhileTheActiveKeyKeepsSigning()
    {
        var keyRing = server.Services.GetRequiredService<TokenKeyRing>();
        var refresher = server.Services.GetRequiredService<TokenKeyRefresher>();
        var activeKeyId = keyRing.Current!.ActiveKey(TokenKeyUse.Signing).KeyId;

        var now = DateTimeOffset.UtcNow;
        var pendingKey = server.Services.GetRequiredService<TokenKeyFactory>()
            .Create(new TokenKeySchedule(TokenKeyUse.Signing, now.AddDays(1), now.AddDays(91), now.AddDays(181)));

        await server.QueryDatabaseAsync(context =>
        {
            context.TokenKeys.Add(pendingKey);
            return context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        try
        {
            (await refresher.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

            using var client = server.CreateClient();
            using var jwks = await client.GetAsync("/.well-known/jwks", TestContext.Current.CancellationToken);
            var publishedKeyIds = (await jwks.ReadJsonAsync()).GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("kid").GetString());
            publishedKeyIds.ShouldContain(activeKeyId);
            publishedKeyIds.ShouldContain(pendingKey.KeyId);

            var token = new JsonWebToken(await client.RequestClientCredentialsTokenAsync(await server.CreateServiceClientAsync()));
            token.Kid.ShouldBe(activeKeyId);
        }
        finally
        {
            await server.QueryDatabaseAsync(context => context.TokenKeys
                .Where(key => key.KeyId == pendingKey.KeyId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken));
            await refresher.RefreshAsync(TestContext.Current.CancellationToken);
        }
    }
}
