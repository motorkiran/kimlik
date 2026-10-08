namespace Kimlik.Contracts.Account;

/// <summary>An organization the signed-in user belongs to, with their roles and permissions in it.</summary>
public sealed record MyOrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    DateTimeOffset JoinedAt);

/// <summary>A pending invitation to the signed-in user's verified email address.</summary>
public sealed record MyInvitationResponse(Guid Id, Guid OrganizationId, string OrganizationName, IReadOnlyList<string> Roles, DateTimeOffset ExpiresAt);
