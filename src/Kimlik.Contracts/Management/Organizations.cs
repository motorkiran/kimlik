using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

/// <summary>A group of users, such as a company, workspace or team. Clients may name it by ID or slug.</summary>
public sealed record OrganizationResponse(Guid Id, string Name, string Slug, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateOrganizationRequest
{
    [Required]
    [StringLength(100)]
    public required string Name { get; init; }

    /// <summary>Up to 64 lowercase letters, digits and inner hyphens, such as <c>acme</c>.</summary>
    [Required]
    [StringLength(64)]
    public required string Slug { get; init; }
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

    /// <summary>Whether the request sets <see cref="Name"/>.</summary>
    [JsonIgnore]
    public bool HasName { get; private init; }

    /// <summary>Whether the request sets <see cref="Slug"/>.</summary>
    [JsonIgnore]
    public bool HasSlug { get; private init; }
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
