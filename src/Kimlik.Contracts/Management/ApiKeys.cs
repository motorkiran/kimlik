using System.ComponentModel.DataAnnotations;

namespace Kimlik.Contracts.Management;

/// <summary>
/// An API key, without its secret. It belongs to either a user or an organization; <c>Prefix</c> is the start of the
/// secret, to recognize the key by.
/// </summary>
public sealed record ApiKeyResponse(
    Guid Id,
    string Name,
    string Prefix,
    Guid? UserId,
    Guid? OrganizationId,
    IReadOnlyList<string> Permissions,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

/// <summary>A new API key with its secret <c>Key</c>, which is shown only this once. Callers send it as <c>Authorization: Bearer kmk_…</c>.</summary>
public sealed record CreatedApiKeyResponse(ApiKeyResponse ApiKey, string Key);

public sealed record CreateApiKeyRequest
{
    [Required]
    [StringLength(100)]
    public required string Name { get; init; }

    /// <summary>Permissions of the application that the key may use; its creator must hold them.</summary>
    [Required]
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>When the key stops working; it does not expire when omitted.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record VerifyApiKeyRequest
{
    [Required]
    [StringLength(256)]
    public required string Key { get; init; }
}

/// <summary>
/// What a resource server needs to know about an API key it was given. An unknown, expired or revoked key, or the key
/// of a user who cannot sign in, is not <c>Active</c>, and nothing else is said about it. <c>Permissions</c> are what
/// the key may do now: for a user's key, its permissions that the user still holds; for an organization's key, its
/// permissions. <c>Plan</c> is the key of the owner's plan, as in the <c>plan</c> claim of tokens.
/// </summary>
public sealed record ApiKeyVerificationResponse(
    bool Active,
    Guid? Id = null,
    Guid? UserId = null,
    Guid? OrganizationId = null,
    IReadOnlyList<string>? Permissions = null,
    string? Plan = null,
    DateTimeOffset? ExpiresAt = null);
