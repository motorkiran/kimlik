using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>
/// Keeps the token keys of this instance in sync with the database. Instances coordinate through an advisory
/// lock, so only one of them creates or deletes keys at a time and the others load the result.
/// </summary>
internal sealed partial class TokenKeyRing(
    IServiceScopeFactory scopeFactory,
    TokenKeyFactory factory,
    IOptions<TokenKeyOptions> options,
    TimeProvider timeProvider,
    ILogger<TokenKeyRing> logger) : ITokenKeyStatus
{
    private volatile TokenKeySet? _current;

    /// <summary>The loaded keys, or <see langword="null"/> until the first successful refresh.</summary>
    public TokenKeySet? Current => _current;

    public bool IsLoaded => _current is not null;

    /// <summary>Applies the rotation schedule and reloads the keys.</summary>
    /// <returns><see langword="true"/> when the usable keys or the active keys changed.</returns>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var keys = await ApplyScheduleAsync(now, cancellationToken);

        var previous = _current;
        var next = TokenKeySet.Create(keys, now, previous, factory.Load);
        if (previous?.Fingerprint == next.Fingerprint)
        {
            return false;
        }

        _current = next;
        return true;
    }

    private async Task<List<TokenKey>> ApplyScheduleAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<KimlikDbContext>();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKeys.TokenKeys})", cancellationToken);

        var keys = await context.TokenKeys.ToListAsync(cancellationToken);
        var plan = TokenKeyPlanner.Plan(keys, now, options.Value);

        if (plan.HasChanges)
        {
            var createdKeys = plan.KeysToCreate.Select(factory.Create).ToList();
            context.TokenKeys.AddRange(createdKeys);
            context.TokenKeys.RemoveRange(plan.KeysToDelete);
            await context.SaveChangesAsync(cancellationToken);

            foreach (var key in createdKeys)
            {
                LogKeyCreated(logger, key.Use, key.KeyId, key.ActivatesAt);
            }

            foreach (var key in plan.KeysToDelete)
            {
                LogKeyDeleted(logger, key.Use, key.KeyId);
            }

            keys = [.. keys.Except(plan.KeysToDelete), .. createdKeys];
        }

        await transaction.CommitAsync(cancellationToken);
        return keys;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created {Use} token key {KeyId}, active from {ActivatesAt:O}")]
    private static partial void LogKeyCreated(ILogger logger, TokenKeyUse use, string keyId, DateTimeOffset activatesAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted expired {Use} token key {KeyId}")]
    private static partial void LogKeyDeleted(ILogger logger, TokenKeyUse use, string keyId);
}
