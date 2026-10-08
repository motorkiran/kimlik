using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Outbox;

/// <summary>
/// Delivers pending outbox messages. Each batch is claimed with <c>FOR UPDATE SKIP LOCKED</c>, so any number
/// of instances can run the dispatcher without delivering a message twice at the same time.
/// </summary>
internal sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    OutboxMessageTypes types,
    OutboxSignal signal,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const int LastErrorMaxLength = 2000;
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) == options.Value.BatchSize)
                {
                    // A full batch means more messages may be waiting.
                }

                await DeleteDeliveredMessagesAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The database may be unavailable; try again at the next poll.
                LogDispatchFailed(logger, exception);
            }

            await signal.WaitAsync(options.Value.PollingInterval, stoppingToken);
        }
    }

    /// <returns>The number of messages claimed.</returns>
    private async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<KimlikDbContext>();
        var now = timeProvider.GetUtcNow();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var messages = await context.OutboxMessages
            .FromSql($"""
                SELECT * FROM kimlik.outbox_messages
                WHERE status = {nameof(OutboxMessageStatus.Pending)} AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {options.Value.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            await DeliverAsync(message, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return messages.Count;
    }

    private async Task DeliverAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            // Each message gets its own scope, so a handler's unit of work never mixes with the dispatcher's.
            await using var handlerScope = scopeFactory.CreateAsyncScope();
            await types.DispatchAsync(message.Type, message.Payload, handlerScope.ServiceProvider, cancellationToken);

            message.Status = OutboxMessageStatus.Processed;
            message.ProcessedAt = timeProvider.GetUtcNow();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            message.Attempts++;
            message.LastError = exception.Message.Length > LastErrorMaxLength ? exception.Message[..LastErrorMaxLength] : exception.Message;

            if (message.Attempts >= options.Value.MaxAttempts)
            {
                message.Status = OutboxMessageStatus.Failed;
                LogMessageFailed(logger, exception, message.Id, message.Type, message.Attempts);
            }
            else
            {
                var delay = TimeSpan.FromTicks(Math.Min(FirstRetryDelay.Ticks << (message.Attempts - 1), MaxRetryDelay.Ticks));
                message.NextAttemptAt = timeProvider.GetUtcNow() + delay;
                LogMessageRetrying(logger, exception, message.Id, message.Type, delay);
            }
        }
    }

    private async Task DeleteDeliveredMessagesAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now < _nextCleanup)
        {
            return;
        }

        _nextCleanup = now + CleanupInterval;

        await using var scope = scopeFactory.CreateAsyncScope();
        var cutoff = now - options.Value.RetentionPeriod;
        await scope.ServiceProvider.GetRequiredService<KimlikDbContext>().OutboxMessages
            .Where(message => message.Status == OutboxMessageStatus.Processed && message.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Dispatching outbox messages failed")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) failed; retrying in {Delay}")]
    private static partial void LogMessageRetrying(ILogger logger, Exception exception, Guid messageId, string type, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) failed after {Attempts} attempts and will not be retried")]
    private static partial void LogMessageFailed(ILogger logger, Exception exception, Guid messageId, string type, int attempts);
}
