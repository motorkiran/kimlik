using System.Text.Json;

namespace Kimlik.Contracts.Management;

public enum AuditActorType
{
    /// <summary>An unauthenticated caller, such as someone attempting to sign in.</summary>
    Anonymous,

    User,

    Client,

    /// <summary>Kimlik itself, for example a background job.</summary>
    System,
}

/// <summary>
/// A security-relevant action. <c>action</c> reads <c>resource.past_tense_verb</c>, such as <c>user.suspended</c>.
/// People are referenced by ID only, so the trail outlives their personal data.
/// </summary>
public sealed record AuditEventResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    AuditActorType ActorType,
    string? ActorId,
    string? SubjectType,
    string? SubjectId,
    Guid? OrganizationId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    JsonElement? Data);
