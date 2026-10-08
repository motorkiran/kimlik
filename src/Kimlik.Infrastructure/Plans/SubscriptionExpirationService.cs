using Kimlik.Application.Plans;
using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Plans;

/// <summary>
/// Marks subscriptions whose trial or period is over as expired, so their status and audit trail say so. One
/// instance does it at a time; the others skip the round.
/// </summary>
internal sealed partial class SubscriptionExpirationService(
    IServiceScopeFactory scopeFactory,
    IOptions<PlanOptions> options,
    ILogger<SubscriptionExpirationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ExpirationInterval);

        do
        {
            try
            {
                await ExpireAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The database may be unavailable; try again at the next tick.
                LogExpirationFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExpireAsync(CancellationToken cancellationToken)
    {
        int expired;
        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<KimlikDbContext>();

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var locked = await context.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({AdvisoryLockKeys.SubscriptionExpiration}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!locked)
            {
                return;
            }

            expired = await scope.ServiceProvider.GetRequiredService<ExpireSubscriptionsHandler>().HandleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            if (expired > 0)
            {
                LogExpired(logger, expired);
            }
        }
        while (expired == ExpireSubscriptionsHandler.BatchSize);
    }

    [LoggerMessage(LogLevel.Information, "Marked {Count} subscriptions as expired")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(LogLevel.Warning, "Expiring subscriptions failed; trying again later")]
    private static partial void LogExpirationFailed(ILogger logger, Exception exception);
}
