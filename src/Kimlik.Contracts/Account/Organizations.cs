using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Account;

/// <summary>
/// An organization the signed-in user belongs to, with their roles and permissions in it, and the public metadata
/// that the application keeps about it.
/// </summary>
public sealed record MyOrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    DateTimeOffset JoinedAt,
    bool RequireMfa,
    JsonObject PublicMetadata);

/// <summary>A pending invitation to the signed-in user's verified email address.</summary>
public sealed record MyInvitationResponse(Guid Id, Guid OrganizationId, string OrganizationName, IReadOnlyList<string> Roles, DateTimeOffset ExpiresAt);

/// <summary>An organization the signed-in user creates; metadata is the application's, so only the Management API sets it.</summary>
public sealed record CreateMyOrganizationRequest
{
    [Required]
    [StringLength(100)]
    public required string Name { get; init; }

    /// <summary>Up to 64 lowercase letters, digits and inner hyphens, such as <c>acme</c>.</summary>
    [Required]
    [StringLength(64)]
    public required string Slug { get; init; }

    /// <summary>Whether signing in to the organization takes a second factor.</summary>
    public bool RequireMfa { get; init; }
}

/// <summary>
/// Changes an organization with JSON Merge Patch semantics: an omitted property keeps its value. Metadata is the
/// application's, so only the Management API changes it.
/// </summary>
public sealed record UpdateMyOrganizationRequest
{
    [StringLength(100)]
    public string? Name
    {
        get;
        init
        {
            field = value;
            HasName = true;
        }
    }

    /// <summary>A new slug; clients that use the old one must switch to it.</summary>
    [StringLength(64)]
    public string? Slug
    {
        get;
        init
        {
            field = value;
            HasSlug = true;
        }
    }

    /// <summary>Whether signing in to the organization takes a second factor.</summary>
    public bool? RequireMfa { get; init; }

    /// <summary>Whether the request sets <see cref="Name"/>.</summary>
    [JsonIgnore]
    public bool HasName { get; private init; }

    /// <summary>Whether the request sets <see cref="Slug"/>.</summary>
    [JsonIgnore]
    public bool HasSlug { get; private init; }
}
