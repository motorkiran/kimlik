using System.Text.Json.Nodes;
using Kimlik.Contracts.Management;

namespace Kimlik.Contracts.Account;

/// <summary>
/// What Kimlik holds about a user, for their right of access (KVKK article 11, GDPR article 15). <c>Activity</c> is the
/// audit events about the user or by them, newest first; the private metadata is only in exports that administrators
/// make.
/// </summary>
public sealed record PersonalDataExport(
    DateTimeOffset ExportedAt,
    ProfileResponse Profile,
    JsonObject? PrivateMetadata,
    IReadOnlyList<MyOrganizationResponse> Organizations,
    IReadOnlyList<UserLoginResponse> Logins,
    IReadOnlyList<PasskeyResponse> Passkeys,
    IReadOnlyList<SessionResponse> Sessions,
    IReadOnlyList<ApiKeyResponse> ApiKeys,
    IReadOnlyList<SubscriptionResponse> Subscriptions,
    IReadOnlyList<AuditEventResponse> Activity);
