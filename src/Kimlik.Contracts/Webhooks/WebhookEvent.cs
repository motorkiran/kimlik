using System.Text.Json;

namespace Kimlik.Contracts.Webhooks;

/// <summary>
/// The body of a webhook: what happened and to what. Events are thin: they name what changed and leave its current
/// state to the Management API, so a delayed or repeated delivery never carries stale data.
/// </summary>
public sealed record WebhookEvent(string Type, DateTimeOffset Timestamp, WebhookEventData Data);

/// <summary>
/// What an event is about, such as a <c>user</c> and its ID; the organization it happened in, if any; who did it;
/// and details that depend on the event type, such as the roles of a new membership.
/// </summary>
public sealed record WebhookEventData(
    string SubjectType, string SubjectId, Guid? OrganizationId, WebhookEventActor Actor, JsonElement? Details = null);

/// <summary>Who caused an event: a <c>user</c>, a <c>client</c>, <c>kimlik</c> itself or someone <c>anonymous</c>.</summary>
public sealed record WebhookEventActor(string Type, string? Id);

/// <summary>The events Kimlik sends to webhook endpoints, named <c>resource.past_tense_verb</c>.</summary>
public static class WebhookEventTypes
{
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserDeleted = "user.deleted";
    public const string UserSuspended = "user.suspended";
    public const string UserReactivated = "user.reactivated";
    public const string UserMfaEnabled = "user.mfa_enabled";
    public const string UserMfaDisabled = "user.mfa_disabled";
    public const string OrganizationCreated = "organization.created";
    public const string OrganizationUpdated = "organization.updated";
    public const string OrganizationDeleted = "organization.deleted";
    public const string MembershipCreated = "membership.created";
    public const string MembershipUpdated = "membership.updated";
    public const string MembershipDeleted = "membership.deleted";
    public const string InvitationCreated = "invitation.created";
    public const string InvitationAccepted = "invitation.accepted";
    public const string SubscriptionCreated = "subscription.created";
    public const string SubscriptionUpdated = "subscription.updated";
    public const string SubscriptionCanceled = "subscription.canceled";
    public const string SubscriptionExpired = "subscription.expired";
    public const string ApiKeyCreated = "api_key.created";
    public const string ApiKeyRevoked = "api_key.revoked";

    /// <summary>Sent only on request, to try an endpoint out; endpoints need not subscribe to it.</summary>
    public const string Test = "webhook.test";

    /// <summary>Every event type that endpoints can subscribe to.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        UserCreated, UserUpdated, UserDeleted, UserSuspended, UserReactivated, UserMfaEnabled, UserMfaDisabled,
        OrganizationCreated, OrganizationUpdated, OrganizationDeleted,
        MembershipCreated, MembershipUpdated, MembershipDeleted,
        InvitationCreated, InvitationAccepted,
        SubscriptionCreated, SubscriptionUpdated, SubscriptionCanceled, SubscriptionExpired,
        ApiKeyCreated, ApiKeyRevoked,
    };
}
