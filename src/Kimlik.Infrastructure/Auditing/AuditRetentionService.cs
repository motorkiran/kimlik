using Kimlik.Application.Auditing;
using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kimlik.Infrastructure.Auditing;

/// <summary>
/// Deletes audit events once they are older than the retention period, every hour, in batches. One instance does it
/// at a time; the others skip the round.
/// </summary>
internal sealed partial class AuditRetentionService(IServiceScopeFactory scopeFactory, ILogger<AuditRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await DeleteAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The database may be unavailable; try again at the next tick.
                LogDeletionFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        int deleted;
        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<KimlikDbContext>();

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var locked = await context.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({AdvisoryLockKeys.AuditRetention}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!locked)
            {
                return;
            }

            deleted = await scope.ServiceProvider.GetRequiredService<DeleteOldAuditEventsHandler>().HandleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            if (deleted > 0)
            {
                LogDeleted(logger, deleted);
            }
        }
        while (deleted == DeleteOldAuditEventsHandler.BatchSize);
    }

    [LoggerMessage(LogLevel.Information, "Deleted {Count} audit events past the retention period")]
    private static partial void LogDeleted(ILogger logger, int count);

    [LoggerMessage(LogLevel.Warning, "Deleting old audit events failed; trying again later")]
    private static partial void LogDeletionFailed(ILogger logger, Exception exception);
}
