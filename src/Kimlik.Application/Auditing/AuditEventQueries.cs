using System.Text.Json;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;
using AuditActorType = Kimlik.Contracts.Management.AuditActorType;
using DomainAuditActorType = Kimlik.Domain.Auditing.AuditActorType;

namespace Kimlik.Application.Auditing;

/// <summary>Filters the audit trail; every filter is optional and they combine. <c>From</c> is inclusive, <c>To</c> exclusive.</summary>
public sealed record ListAuditEventsQuery(
    string? Action,
    AuditActorType? ActorType,
    string? ActorId,
    string? SubjectType,
    string? SubjectId,
    Guid? OrganizationId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Limit);

/// <summary>Reads the audit trail, newest first.</summary>
public sealed class ListAuditEventsHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<AuditEventResponse>>> HandleAsync(ListAuditEventsQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var before))
        {
            return CommonErrors.InvalidCursor;
        }

        if (query.ActorType is { } requestedActorType && !Enum.IsDefined(requestedActorType))
        {
            return CommonErrors.InvalidParameter("actorType");
        }

        var events = context.AuditEvents.AsNoTracking();

        if (before is { } beforeId)
        {
            events = events.Where(auditEvent => auditEvent.Id < beforeId);
        }

        if (query.Action is { Length: > 0 } action)
        {
            events = events.Where(auditEvent => auditEvent.Action == action);
        }

        if (query.ActorType is { } actorType)
        {
            var domainActorType = Enum.Parse<DomainAuditActorType>(actorType.ToString());
            events = events.Where(auditEvent => auditEvent.ActorType == domainActorType);
        }

        if (query.ActorId is { Length: > 0 } actorId)
        {
            events = events.Where(auditEvent => auditEvent.ActorId == actorId);
        }

        if (query.SubjectType is { Length: > 0 } subjectType)
        {
            events = events.Where(auditEvent => auditEvent.SubjectType == subjectType);
        }

        if (query.SubjectId is { Length: > 0 } subjectId)
        {
            events = events.Where(auditEvent => auditEvent.SubjectId == subjectId);
        }

        if (query.OrganizationId is { } organizationId)
        {
            events = events.Where(auditEvent => auditEvent.OrganizationId == organizationId);
        }

        if (query.From is { } from)
        {
            events = events.Where(auditEvent => auditEvent.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            events = events.Where(auditEvent => auditEvent.OccurredAt < to);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(
            events.OrderByDescending(auditEvent => auditEvent.Id), query.Limit, auditEvent => auditEvent.Id, cancellationToken);

        return new Page<AuditEventResponse>([.. page.Select(ToResponse)], nextCursor);
    }

    internal static AuditEventResponse ToResponse(AuditEvent auditEvent) => new(
        auditEvent.Id,
        auditEvent.OccurredAt,
        auditEvent.Action,
        Enum.Parse<AuditActorType>(auditEvent.ActorType.ToString()),
        auditEvent.ActorId,
        auditEvent.SubjectType,
        auditEvent.SubjectId,
        auditEvent.OrganizationId,
        auditEvent.IpAddress?.ToString(),
        auditEvent.UserAgent,
        auditEvent.CorrelationId,
        auditEvent.Data is null ? null : JsonSerializer.Deserialize<JsonElement>(auditEvent.Data));
}
