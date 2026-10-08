using System.Security.Cryptography;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Webhooks;
using Kimlik.Contracts.Webhooks;
using Kimlik.Domain.Webhooks;
using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Webhooks;

/// <summary>
/// Sends due webhook deliveries and retries failed ones with growing delays. A batch is claimed in a short
/// transaction that leases its deliveries (<c>FOR UPDATE SKIP LOCKED</c>), and sent outside of it, so any number of
/// instances can share the work. An instance that stops hands back what it has not sent; if one stops without that,
/// such as in a crash, the lease runs out and another one retries: delivery is at least once.
/// </summary>
internal sealed partial class WebhookDeliveryService(
    IServiceScopeFactory scopeFactory,
    WebhookSignal signal,
    IOptions<WebhookOptions> options,
    TimeProvider timeProvider,
    ILogger<WebhookDeliveryService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await SendBatchAsync(stoppingToken) == options.Value.BatchSize)
                {
                    // A full batch means more deliveries may be due.
                }

                await DeleteOldDeliveriesAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The database may be unavailable; try again at the next poll.
                LogSendingFailed(logger, exception);
            }

            await signal.WaitAsync(options.Value.PollingInterval, stoppingToken);
        }
    }

    /// <returns>The number of deliveries attempted.</returns>
    private async Task<int> SendBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<KimlikDbContext>();
        var deliveries = await ClaimAsync(context, cancellationToken);
        if (deliveries.Count == 0)
        {
            return 0;
        }

        var endpointIds = deliveries.Select(delivery => delivery.EndpointId).Distinct().ToList();
        var endpoints = await context.WebhookEndpoints.Where(endpoint => endpointIds.Contains(endpoint.Id)).ToDictionaryAsync(endpoint => endpoint.Id, cancellationToken);
        var sender = scope.ServiceProvider.GetRequiredService<WebhookSender>();
        var encryption = scope.ServiceProvider.GetRequiredService<ISecretEncryption>();

        var sends = deliveries
            .Select(delivery => (Delivery: delivery, Attempt: AttemptAsync(delivery, endpoints[delivery.EndpointId], sender, encryption, cancellationToken)))
            .ToList();
        try
        {
            await Task.WhenAll(sends.Select(send => send.Attempt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The instance is stopping; what was not sent is handed back below.
        }

        var now = timeProvider.GetUtcNow();
        foreach (var (delivery, attempt) in sends)
        {
            if (attempt.IsCompletedSuccessfully)
            {
                Record(delivery, endpoints[delivery.EndpointId], attempt.Result, now);
            }
            else
            {
                // Another instance makes the attempt now, instead of when the lease runs out.
                delivery.AbandonAttempt(now);
            }
        }

        // Saved even while stopping, so that neither the outcomes nor the handed-back deliveries are lost.
        await context.SaveChangesAsync(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        return deliveries.Count;
    }

    /// <summary>Leases a batch of due deliveries for long enough to send them.</summary>
    private async Task<List<WebhookDelivery>> ClaimAsync(KimlikDbContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now + options.Value.Timeout + TimeSpan.FromMinutes(1);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var deliveries = await context.WebhookDeliveries
            .FromSql($"""
                SELECT * FROM kimlik.webhook_deliveries
                WHERE status = 'Pending' AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {options.Value.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var delivery in deliveries)
        {
            delivery.BeginAttempt(now, leaseUntil);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deliveries;
    }

    private static async Task<WebhookAttempt?> AttemptAsync(
        WebhookDelivery delivery, WebhookEndpoint endpoint, WebhookSender sender, ISecretEncryption encryption, CancellationToken cancellationToken)
    {
        // Test events go even to disabled endpoints: trying one out comes before turning it on.
        if (!endpoint.Enabled && delivery.EventType != WebhookEventTypes.Test)
        {
            return null;
        }

        string secret;
        try
        {
            secret = encryption.Decrypt(endpoint.EncryptedSecret, WebhookSecrets.Purpose, endpoint.Id);
        }
        catch (CryptographicException)
        {
            return new WebhookAttempt(false, null, null, "The endpoint's secret cannot be decrypted with the current master key; rotate it.");
        }

        return await sender.SendAsync(endpoint.Url, secret, delivery, cancellationToken);
    }

    /// <summary>Records the outcome; a failed attempt is retried after the next delay, until none is left.</summary>
    private void Record(WebhookDelivery delivery, WebhookEndpoint endpoint, WebhookAttempt? attempt, DateTimeOffset now)
    {
        if (attempt is null)
        {
            delivery.Fail(null, null, "The endpoint is disabled.", retryAt: null);
            return;
        }

        if (attempt.Succeeded)
        {
            delivery.Succeed(attempt.StatusCode!.Value, attempt.ResponseBody);
            endpoint.RecordSuccess();
            return;
        }

        var delays = options.Value.Delays;
        DateTimeOffset? retryAt = delivery.Attempts <= delays.Count ? now + delays[delivery.Attempts - 1] : null;
        delivery.Fail(attempt.StatusCode, attempt.ResponseBody, attempt.Error, retryAt);
        endpoint.RecordFailure(now);

        if (retryAt is null)
        {
            LogDeliveryFailed(logger, delivery.Id, delivery.EventType, endpoint.Url, delivery.Attempts);
        }
    }

    private async Task DeleteOldDeliveriesAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now < _nextCleanup)
        {
            return;
        }

        _nextCleanup = now + CleanupInterval;

        await using var scope = scopeFactory.CreateAsyncScope();
        var cutoff = now - options.Value.RetentionPeriod;
        await scope.ServiceProvider.GetRequiredService<KimlikDbContext>().WebhookDeliveries
            .Where(delivery => delivery.Status != WebhookDeliveryStatus.Pending && delivery.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    [LoggerMessage(LogLevel.Error, "Sending webhooks failed")]
    private static partial void LogSendingFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Webhook delivery {DeliveryId} ({EventType}) to {Url} failed after {Attempts} attempts and will not be retried")]
    private static partial void LogDeliveryFailed(ILogger logger, Guid deliveryId, string eventType, string url, int attempts);
}
