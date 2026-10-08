using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>Filters for the audit trail; every one is optional, and they combine.</summary>
public sealed record AuditEventQuery
{
    /// <summary>An action such as <c>user.suspended</c>.</summary>
    public string? Action { get; init; }

    public AuditActorType? ActorType { get; init; }

    /// <summary>A user ID or client ID.</summary>
    public string? ActorId { get; init; }

    /// <summary>What was acted on, such as <c>user</c> or <c>role</c>.</summary>
    public string? SubjectType { get; init; }

    public string? SubjectId { get; init; }

    /// <summary>Events within this organization.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Events at or after this time.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Events before this time.</summary>
    public DateTimeOffset? To { get; init; }

    public string? Cursor { get; init; }

    public int? Limit { get; init; }
}

/// <summary>The audit trail, newest first. Requires <c>kimlik.audit:read</c>.</summary>
public sealed class AuditEventsClient
{
    private readonly KimlikHttp _http;

    internal AuditEventsClient(KimlikHttp http) => _http = http;

    public Task<Page<AuditEventResponse>> ListAsync(AuditEventQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new AuditEventQuery();
        var path = KimlikHttp.WithQuery(
            "audit-events",
            ("action", query.Action),
            ("actorType", query.ActorType),
            ("actorId", query.ActorId),
            ("subjectType", query.SubjectType),
            ("subjectId", query.SubjectId),
            ("organizationId", query.OrganizationId),
            ("from", query.From),
            ("to", query.To),
            ("cursor", query.Cursor),
            ("limit", query.Limit));

        return _http.GetAsync<Page<AuditEventResponse>>(path, cancellationToken);
    }
}
