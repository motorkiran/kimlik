using Kimlik.Application.Accounts;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Kimlik.Infrastructure.Persistence;

/// <summary>
/// Deletes the protocol data that can no longer be used once it is two weeks old: expired, redeemed and revoked
/// tokens (social login state tokens among them), and the authorizations of revoked sessions. Until then, a reused
/// refresh token is still recognized and revokes its session. Records of the clients a browser session signed in to
/// go after 90 days, usage counters after 13 months and usage idempotency keys after a day. One instance prunes at a
/// time.
/// </summary>
internal sealed partial class ProtocolDataPruner(
    KimlikDbContext context,
    IOpenIddictTokenManager tokens,
    IOpenIddictAuthorizationManager authorizations,
    TimeProvider timeProvider,
    ILogger<ProtocolDataPruner> logger)
{
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(14);

    public async Task PruneAsync(CancellationToken cancellationToken)
    {
        // A session-level lock, because the stores prune in transactions of their own; the connection stays open
        // so the lock is released on the connection that holds it.
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var locked = await context.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_lock({AdvisoryLockKeys.ProtocolDataPruning}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!locked)
            {
                return;
            }

            try
            {
                var threshold = timeProvider.GetUtcNow() - RetentionPeriod;
                var prunedTokens = await tokens.PruneAsync(threshold, cancellationToken);
                var prunedAuthorizations = await authorizations.PruneAsync(threshold, cancellationToken);
                var sessionsThreshold = timeProvider.GetUtcNow() - BackChannelLogout.RetentionPeriod;
                await context.SessionClients.Where(record => record.SignedInAt < sessionsThreshold).ExecuteDeleteAsync(cancellationToken);

                // Usage counts for 13 months, and idempotency keys for a day.
                var now = timeProvider.GetUtcNow();
                var oldestPeriod = UsageCounter.PeriodOf(now).AddMonths(-12);
                await context.UsageCounters.Where(counter => counter.Period < oldestPeriod).ExecuteDeleteAsync(cancellationToken);
                await context.Set<UsageRecord>().Where(record => record.CreatedAt < now.AddDays(-1)).ExecuteDeleteAsync(cancellationToken);

                if (prunedTokens > 0 || prunedAuthorizations > 0)
                {
                    LogPruned(logger, prunedTokens, prunedAuthorizations);
                }
            }
            finally
            {
                await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({AdvisoryLockKeys.ProtocolDataPruning})", CancellationToken.None);
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [LoggerMessage(LogLevel.Information, "Pruned {Tokens} tokens and {Authorizations} authorizations that can no longer be used")]
    private static partial void LogPruned(ILogger logger, long tokens, long authorizations);
}

/// <summary>Prunes the protocol data every hour.</summary>
internal sealed partial class ProtocolDataPruningService(IServiceScopeFactory scopeFactory, ILogger<ProtocolDataPruningService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ProtocolDataPruner>().PruneAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The database may be unavailable; try again at the next tick.
                LogPruningFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(LogLevel.Warning, "Pruning tokens and authorizations failed; trying again later")]
    private static partial void LogPruningFailed(ILogger logger, Exception exception);
}
