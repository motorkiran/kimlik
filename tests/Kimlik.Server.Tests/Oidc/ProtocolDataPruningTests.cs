using Kimlik.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Tests.Oidc;

public sealed class ProtocolDataPruningTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pruning_DeletesOldTokensThatCanNoLongerBeUsed_AndKeepsTheRest()
    {
        var now = DateTimeOffset.UtcNow;
        var old = now - ProtocolDataPruner.RetentionPeriod - TimeSpan.FromDays(1);

        var expired = await CreateTokenAsync(old, expiresAt: old + TimeSpan.FromHours(1), Statuses.Valid);
        var redeemed = await CreateTokenAsync(old, expiresAt: now + TimeSpan.FromDays(30), Statuses.Redeemed);
        var recentlyRedeemed = await CreateTokenAsync(now - TimeSpan.FromDays(1), expiresAt: now + TimeSpan.FromDays(30), Statuses.Redeemed);
        var stillValid = await CreateTokenAsync(old, expiresAt: now + TimeSpan.FromDays(30), Statuses.Valid);

        await server.WithServicesAsync(services => services.GetRequiredService<ProtocolDataPruner>().PruneAsync(CancellationToken));

        (await ExistsAsync(expired)).ShouldBeFalse();
        (await ExistsAsync(redeemed)).ShouldBeFalse();
        (await ExistsAsync(recentlyRedeemed)).ShouldBeTrue();
        (await ExistsAsync(stillValid)).ShouldBeTrue();
    }

    private Task<string> CreateTokenAsync(DateTimeOffset createdAt, DateTimeOffset expiresAt, string status) =>
        server.WithServicesAsync(async services =>
        {
            var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
            var token = await tokens.CreateAsync(
                new OpenIddictTokenDescriptor
                {
                    CreationDate = createdAt,
                    ExpirationDate = expiresAt,
                    Status = status,
                    Subject = Guid.NewGuid().ToString(),
                    Type = TokenTypeHints.RefreshToken,
                },
                CancellationToken);

            return (await tokens.GetIdAsync(token, CancellationToken))!;
        });

    private Task<bool> ExistsAsync(string id) =>
        server.WithServicesAsync(async services => await services.GetRequiredService<IOpenIddictTokenManager>().FindByIdAsync(id, CancellationToken) is not null);
}
