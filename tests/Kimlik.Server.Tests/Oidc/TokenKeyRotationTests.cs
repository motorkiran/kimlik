using System.Net;
using Kimlik.Contracts;
using Kimlik.Domain.Access;
using Kimlik.Infrastructure.Security.TokenKeys;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Api;
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

    [Fact]
    public async Task NewActiveKey_IsTrustedByTheManagementApi()
    {
        var refresher = server.Services.GetRequiredService<TokenKeyRefresher>();
        var serviceClient = await server.CreateServiceClientAsync(KimlikScopes.Api);
        await server.AssignToClientAsync(serviceClient.ClientId, SystemRoles.Admin);

        // The API has validated tokens before the rotation, so it has to pick up the new key, not just load it.
        (await CallApiAsync(serviceClient)).ShouldBe(HttpStatusCode.OK);

        // Activated after the current key, so it takes over signing with the next refresh.
        var now = DateTimeOffset.UtcNow;
        var newKey = server.Services.GetRequiredService<TokenKeyFactory>()
            .Create(new TokenKeySchedule(TokenKeyUse.Signing, now, now.AddDays(90), now.AddDays(180)));

        await server.QueryDatabaseAsync(context =>
        {
            context.TokenKeys.Add(newKey);
            return context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        try
        {
            (await refresher.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

            (await CallApiAsync(serviceClient, expectedKeyId: newKey.KeyId)).ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await server.QueryDatabaseAsync(context => context.TokenKeys
                .Where(key => key.KeyId == newKey.KeyId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken));
            await refresher.RefreshAsync(TestContext.Current.CancellationToken);
        }
    }

    private async Task<HttpStatusCode> CallApiAsync(TestClient serviceClient, string? expectedKeyId = null)
    {
        using var client = server.CreateClient();
        var token = await client.RequestClientCredentialsTokenAsync(serviceClient, KimlikScopes.Api);
        if (expectedKeyId is not null)
        {
            new JsonWebToken(token).Kid.ShouldBe(expectedKeyId);
        }

        using var api = server.WithToken(token);
        using var response = await api.GetAsync("/api/v1/users", TestContext.Current.CancellationToken);
        return response.StatusCode;
    }
}
