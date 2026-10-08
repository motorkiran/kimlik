using System.Net;

namespace Kimlik.Domain.Auditing;

/// <summary>
/// An immutable record of a security-relevant action. Audit events reference people by ID only,
/// so deleting a user removes their personal data without rewriting the trail.
/// </summary>
public sealed class AuditEvent
{
    public const int ActionMaxLength = 64;
    public const int ReferenceMaxLength = 128;
    public const int UserAgentMaxLength = 512;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>What happened, as <c>resource.past_tense_verb</c>; see <see cref="AuditActions"/>.</summary>
    public required string Action { get; init; }

    public required AuditActorType ActorType { get; init; }

    public string? ActorId { get; init; }

    public string? SubjectType { get; init; }

    public string? SubjectId { get; init; }

    /// <summary>The organization the action happened in, so an organization's trail can be read as a whole.</summary>
    public Guid? OrganizationId { get; init; }

    public IPAddress? IpAddress { get; init; }

    public string? UserAgent { get; init; }

    public string? CorrelationId { get; init; }

    /// <summary>Additional details as a JSON object.</summary>
    public string? Data { get; init; }
}
