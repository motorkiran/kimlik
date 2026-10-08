using System.Text.Json;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Contracts.Webhooks;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using AuditActorType = Kimlik.Domain.Auditing.AuditActorType;
using ContractStatus = Kimlik.Contracts.Management.WebhookDeliveryStatus;
using DomainStatus = Kimlik.Domain.Webhooks.WebhookDeliveryStatus;

namespace Kimlik.Application.Webhooks;

/// <summary>Lists deliveries newest first, optionally of one endpoint or with one status.</summary>
public sealed record ListWebhookDeliveriesQuery(Guid? EndpointId, ContractStatus? Status, string? Cursor, int? Limit);

public sealed class ListWebhookDeliveriesHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<WebhookDeliveryResponse>>> HandleAsync(ListWebhookDeliveriesQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var before))
        {
            return CommonErrors.InvalidCursor;
        }

        if (query.Status is { } requested && !Enum.IsDefined(requested))
        {
            return CommonErrors.InvalidParameter("status");
        }

        var deliveries = context.WebhookDeliveries.AsNoTracking();
        if (before is { } beforeId)
        {
            deliveries = deliveries.Where(delivery => delivery.Id < beforeId);
        }

        if (query.EndpointId is { } endpointId)
        {
            deliveries = deliveries.Where(delivery => delivery.EndpointId == endpointId);
        }

        if (query.Status is { } status)
        {
            var domainStatus = (DomainStatus)status;
            deliveries = deliveries.Where(delivery => delivery.Status == domainStatus);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(deliveries.OrderByDescending(delivery => delivery.Id), query.Limit, delivery => delivery.Id, cancellationToken);
        return new Page<WebhookDeliveryResponse>([.. page.Select(delivery => delivery.ToResponse())], nextCursor);
    }
}

public sealed class GetWebhookDeliveryHandler(IKimlikDbContext context)
{
    public async Task<Result<WebhookDeliveryResponse>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await context.WebhookDeliveries.AsNoTracking().SingleOrDefaultAsync(delivery => delivery.Id == id, cancellationToken) is { } delivery
            ? delivery.ToResponse()
            : WebhookErrors.DeliveryNotFound;
}

/// <summary>Sends a delivered or failed event again, with the same event ID, as a fresh series of attempts.</summary>
public sealed class RedeliverWebhookHandler(IKimlikDbContext context, IWebhookSignal signal, TimeProvider timeProvider)
{
    public async Task<Result<WebhookDeliveryResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await context.WebhookDeliveries.SingleOrDefaultAsync(delivery => delivery.Id == id, cancellationToken) is not { } delivery)
        {
            return WebhookErrors.DeliveryNotFound;
        }

        delivery.Redeliver(timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        signal.Notify();
        return delivery.ToResponse();
    }
}

/// <summary>Sends a <c>webhook.test</c> event to one endpoint, to try it out; it goes even to a disabled endpoint.</summary>
public sealed class SendTestWebhookHandler(IKimlikDbContext context, IRequestContext request, IWebhookSignal signal, TimeProvider timeProvider)
{
    public async Task<Result<WebhookDeliveryResponse>> HandleAsync(Guid endpointId, CancellationToken cancellationToken)
    {
        if (!await context.WebhookEndpoints.AnyAsync(endpoint => endpoint.Id == endpointId, cancellationToken))
        {
            return WebhookErrors.EndpointNotFound;
        }

        var now = timeProvider.GetUtcNow();
        var webhookEvent = new WebhookEvent(
            WebhookEventTypes.Test, now, new WebhookEventData("webhook_endpoint", endpointId.ToString(), null, WebhookEvents.ActorOf(request.Actor)));
        var delivery = WebhookDelivery.Create(endpointId, Guid.CreateVersion7(now), WebhookEventTypes.Test, WebhookEvents.Serialize(webhookEvent), now);

        context.WebhookDeliveries.Add(delivery);
        await context.SaveChangesAsync(cancellationToken);
        signal.Notify();
        return delivery.ToResponse();
    }
}

/// <summary>An event to send to every endpoint that receives its type; written to the outbox with the change it reports.</summary>
[OutboxMessage("webhook.event")]
public sealed record WebhookEventPublished(Guid EventId, string Type, string Payload);

/// <summary>Turns an event into one delivery per endpoint that receives it.</summary>
public sealed class WebhookFanOutHandler(IKimlikDbContext context, IWebhookSignal signal, TimeProvider timeProvider)
    : IOutboxMessageHandler<WebhookEventPublished>
{
    public async Task HandleAsync(WebhookEventPublished message, CancellationToken cancellationToken)
    {
        // A repeated outbox delivery must not send the event twice.
        if (await context.WebhookDeliveries.AnyAsync(delivery => delivery.EventId == message.EventId, cancellationToken))
        {
            return;
        }

        var endpoints = await context.WebhookEndpoints.AsNoTracking().Where(endpoint => endpoint.Enabled).ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var endpoint in endpoints.Where(endpoint => endpoint.Receives(message.Type)))
        {
            context.WebhookDeliveries.Add(WebhookDelivery.Create(endpoint.Id, message.EventId, message.Type, message.Payload, now));
        }

        await context.SaveChangesAsync(cancellationToken);
        signal.Notify();
    }
}

/// <summary>Builds webhook events from audited changes.</summary>
public static class WebhookEvents
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The event for an audit event whose action is a webhook event type, or <see langword="null"/>.</summary>
    public static WebhookEventPublished? From(AuditEvent auditEvent)
    {
        if (!WebhookEventTypes.All.Contains(auditEvent.Action) || auditEvent.SubjectType is null || auditEvent.SubjectId is null)
        {
            return null;
        }

        var details = auditEvent.Data is { } data ? JsonDocument.Parse(data).RootElement.Clone() : (JsonElement?)null;
        var webhookEvent = new WebhookEvent(
            auditEvent.Action,
            auditEvent.OccurredAt,
            new WebhookEventData(auditEvent.SubjectType, auditEvent.SubjectId, auditEvent.OrganizationId, new WebhookEventActor(Name(auditEvent.ActorType), auditEvent.ActorId), details));

        return new WebhookEventPublished(auditEvent.Id, auditEvent.Action, Serialize(webhookEvent));
    }

    internal static WebhookEventActor ActorOf(AuditActor actor) => new(Name(actor.Type), actor.Id);

    internal static string Serialize(WebhookEvent webhookEvent) => JsonSerializer.Serialize(webhookEvent, SerializerOptions);

    private static string Name(AuditActorType type) => type switch
    {
        AuditActorType.User => "user",
        AuditActorType.Client => "client",
        AuditActorType.System => "kimlik",
        _ => "anonymous",
    };
}

internal static class WebhookDeliveryMappings
{
    public static WebhookDeliveryResponse ToResponse(this WebhookDelivery delivery) => new(
        delivery.Id,
        delivery.EndpointId,
        delivery.EventId,
        delivery.EventType,
        (ContractStatus)delivery.Status,
        delivery.Attempts,
        delivery.CreatedAt,
        delivery.LastAttemptAt,
        delivery.Status == DomainStatus.Pending ? delivery.NextAttemptAt : null,
        delivery.ResponseStatusCode,
        delivery.ResponseBody,
        delivery.Error,
        JsonDocument.Parse(delivery.Payload).RootElement.Clone());
}
