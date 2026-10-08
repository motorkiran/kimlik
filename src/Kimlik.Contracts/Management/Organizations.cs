using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

/// <summary>
/// A group of users, such as a company, workspace or team. Clients may name it by ID or slug. Metadata is what the
/// application keeps about it: members can read the public metadata through the Account API, and only the Management
/// API reads the private metadata.
/// </summary>
public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    bool RequireMfa,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    JsonObject PublicMetadata,
    JsonObject PrivateMetadata);

public sealed record CreateOrganizationRequest
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

    /// <summary>A JSON object of up to 8 KB that members can read too.</summary>
    public JsonObject? PublicMetadata { get; init; }

    /// <summary>A JSON object of up to 8 KB for the application's backend only.</summary>
    public JsonObject? PrivateMetadata { get; init; }
}

/// <summary>Changes an organization with JSON Merge Patch semantics: an omitted property keeps its value.</summary>
public sealed record UpdateOrganizationRequest
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

    /// <summary>Replaces the public metadata, a JSON object of up to 8 KB; <c>null</c> clears it.</summary>
    public JsonObject? PublicMetadata
    {
        get;
        init
        {
            field = value;
            HasPublicMetadata = true;
        }
    }

    /// <summary>Replaces the private metadata, a JSON object of up to 8 KB; <c>null</c> clears it.</summary>
    public JsonObject? PrivateMetadata
    {
        get;
        init
        {
            field = value;
            HasPrivateMetadata = true;
        }
    }

    /// <summary>Whether the request sets <see cref="Name"/>.</summary>
    [JsonIgnore]
    public bool HasName { get; private init; }

    /// <summary>Whether the request sets <see cref="Slug"/>.</summary>
    [JsonIgnore]
    public bool HasSlug { get; private init; }

    /// <summary>Whether the request sets <see cref="PublicMetadata"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasPublicMetadata { get; private init; }

    /// <summary>Whether the request sets <see cref="PrivateMetadata"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasPrivateMetadata { get; private init; }
}

/// <summary>A member of an organization and the organization roles they hold there.</summary>
public sealed record MemberResponse(Guid UserId, string? Email, string? Name, IReadOnlyList<string> Roles, DateTimeOffset JoinedAt);

public sealed record AddMemberRequest
{
    public required Guid UserId { get; init; }

    /// <summary>Keys of organization roles.</summary>
    [MaxLength(50)]
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public enum InvitationStatus
{
    Pending,
    Accepted,
    Revoked,
    Expired,
}

/// <summary>An invitation to join an organization, sent by email. Accepting it adds the roles to the new member.</summary>
public sealed record InvitationResponse(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    InvitationStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record CreateInvitationRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public required string Email { get; init; }

    /// <summary>Keys of the organization roles the invited user gets on joining.</summary>
    [MaxLength(50)]
    public IReadOnlyList<string> Roles { get; init; } = [];
}
