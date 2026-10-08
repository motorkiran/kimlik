using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Contracts.Webhooks;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Webhooks;

public sealed record ListWebhookEndpointsQuery(string? Cursor, int? Limit);

public sealed class ListWebhookEndpointsHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<WebhookEndpointResponse>>> HandleAsync(ListWebhookEndpointsQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        var endpoints = context.WebhookEndpoints.AsNoTracking();
        if (after is { } afterId)
        {
            endpoints = endpoints.Where(endpoint => endpoint.Id > afterId);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(endpoints.OrderBy(endpoint => endpoint.Id), query.Limit, endpoint => endpoint.Id, cancellationToken);
        return new Page<WebhookEndpointResponse>([.. page.Select(endpoint => endpoint.ToResponse())], nextCursor);
    }
}

public sealed class GetWebhookEndpointHandler(IKimlikDbContext context)
{
    public async Task<Result<WebhookEndpointResponse>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await context.WebhookEndpoints.AsNoTracking().SingleOrDefaultAsync(endpoint => endpoint.Id == id, cancellationToken) is { } endpoint
            ? endpoint.ToResponse()
            : WebhookErrors.EndpointNotFound;
}

public sealed class CreateWebhookEndpointHandler(IKimlikDbContext context, ISecretEncryption encryption, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<CreatedWebhookEndpointResponse>> HandleAsync(CreateWebhookEndpointRequest request, CancellationToken cancellationToken)
    {
        var eventTypes = request.EventTypes ?? [];
        if (!eventTypes.All(WebhookEventTypes.All.Contains))
        {
            return WebhookErrors.UnknownEventType;
        }

        var now = timeProvider.GetUtcNow();
        var created = WebhookEndpoint.Create(request.Url, request.Description, eventTypes, request.Enabled, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var endpoint = created.Value;
        var secret = WebhookSecrets.Generate();
        endpoint.SetSecret(encryption.Encrypt(secret, WebhookSecrets.Purpose, endpoint.Id), now);

        context.WebhookEndpoints.Add(endpoint);
        auditLog.Record(AuditActions.WebhookEndpointCreated, AuditSubject.WebhookEndpoint(endpoint.Id), new Dictionary<string, object?> { ["url"] = endpoint.Url });
        await context.SaveChangesAsync(cancellationToken);

        return new CreatedWebhookEndpointResponse(endpoint.ToResponse(), secret);
    }
}

public sealed class UpdateWebhookEndpointHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<WebhookEndpointResponse>> HandleAsync(Guid id, UpdateWebhookEndpointRequest request, CancellationToken cancellationToken)
    {
        if (await context.WebhookEndpoints.SingleOrDefaultAsync(endpoint => endpoint.Id == id, cancellationToken) is not { } endpoint)
        {
            return WebhookErrors.EndpointNotFound;
        }

        IReadOnlyList<string> eventTypes = request.HasEventTypes ? request.EventTypes ?? [] : endpoint.EventTypes;
        if (!eventTypes.All(WebhookEventTypes.All.Contains))
        {
            return WebhookErrors.UnknownEventType;
        }

        var updated = endpoint.Update(
            request.HasUrl ? request.Url ?? string.Empty : endpoint.Url,
            request.HasDescription ? request.Description : endpoint.Description,
            eventTypes,
            request.Enabled ?? endpoint.Enabled,
            timeProvider.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        auditLog.Record(AuditActions.WebhookEndpointUpdated, AuditSubject.WebhookEndpoint(id), new Dictionary<string, object?> { ["url"] = endpoint.Url });
        await context.SaveChangesAsync(cancellationToken);
        return endpoint.ToResponse();
    }
}

/// <summary>Deletes an endpoint with its delivery log; deliveries under way are dropped.</summary>
public sealed class DeleteWebhookEndpointHandler(IKimlikDbContext context, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await context.WebhookEndpoints.SingleOrDefaultAsync(endpoint => endpoint.Id == id, cancellationToken) is not { } endpoint)
        {
            return WebhookErrors.EndpointNotFound;
        }

        context.WebhookEndpoints.Remove(endpoint);
        auditLog.Record(AuditActions.WebhookEndpointDeleted, AuditSubject.WebhookEndpoint(id), new Dictionary<string, object?> { ["url"] = endpoint.Url });
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Replaces an endpoint's signing secret. Deliveries from then on, retries included, are signed with the new one.</summary>
public sealed class RotateWebhookSecretHandler(IKimlikDbContext context, ISecretEncryption encryption, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<WebhookSecretResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await context.WebhookEndpoints.SingleOrDefaultAsync(endpoint => endpoint.Id == id, cancellationToken) is not { } endpoint)
        {
            return WebhookErrors.EndpointNotFound;
        }

        var secret = WebhookSecrets.Generate();
        endpoint.SetSecret(encryption.Encrypt(secret, WebhookSecrets.Purpose, endpoint.Id), timeProvider.GetUtcNow());
        auditLog.Record(AuditActions.WebhookEndpointSecretRotated, AuditSubject.WebhookEndpoint(id));
        await context.SaveChangesAsync(cancellationToken);
        return new WebhookSecretResponse(secret);
    }
}

internal static class WebhookMappings
{
    public static WebhookEndpointResponse ToResponse(this WebhookEndpoint endpoint) =>
        new(endpoint.Id, endpoint.Url, endpoint.Description, [.. endpoint.EventTypes], endpoint.Enabled, endpoint.CreatedAt, endpoint.UpdatedAt, endpoint.FailingSince);
}
