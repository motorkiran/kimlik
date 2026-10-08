using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

public enum RoleScope
{
    /// <summary>Assigned to users and service clients directly; applies everywhere.</summary>
    Global,

    /// <summary>Assigned through an organization membership; applies within that organization only.</summary>
    Organization,
}

/// <summary>A permission. System permissions (<c>kimlik.*</c>) guard Kimlik itself and cannot be changed.</summary>
public sealed record PermissionResponse(Guid Id, string Key, string? Description, bool IsSystem, DateTimeOffset CreatedAt);

public sealed record CreatePermissionRequest
{
    /// <summary><c>resource:action</c> in lowercase, such as <c>invoices:read</c>. It cannot be changed later.</summary>
    [Required]
    [StringLength(128)]
    public required string Key { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }
}

/// <summary>Changes a permission with JSON Merge Patch semantics: an omitted property keeps its value and <c>null</c> clears it.</summary>
public sealed record UpdatePermissionRequest
{
    [StringLength(256)]
    public string? Description
    {
        get;
        init
        {
            field = value;
            HasDescription = true;
        }
    }

    /// <summary>Whether the request sets <see cref="Description"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasDescription { get; private init; }
}

/// <summary>A role and the keys of its permissions. System roles (<c>kimlik-*</c>) cannot be changed.</summary>
public sealed record RoleResponse(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    RoleScope Scope,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateRoleRequest
{
    /// <summary>Lowercase letters, digits, <c>-</c> and <c>_</c>, starting with a letter. It cannot be changed later.</summary>
    [Required]
    [StringLength(64)]
    public required string Key { get; init; }

    [Required]
    [StringLength(100)]
    public required string Name { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }

    public RoleScope Scope { get; init; } = RoleScope.Global;

    /// <summary>Keys of existing permissions.</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

/// <summary>Changes a role with JSON Merge Patch semantics: an omitted property keeps its value and <c>null</c> clears it.</summary>
public sealed record UpdateRoleRequest
{
    /// <summary>The new name; a role always has one, so it cannot be cleared.</summary>
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

    [StringLength(256)]
    public string? Description
    {
        get;
        init
        {
            field = value;
            HasDescription = true;
        }
    }

    /// <summary>Whether the request sets <see cref="Name"/>.</summary>
    [JsonIgnore]
    public bool HasName { get; private init; }

    /// <summary>Whether the request sets <see cref="Description"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasDescription { get; private init; }
}

/// <summary>Replaces the permissions of a role.</summary>
public sealed record SetPermissionsRequest
{
    /// <summary>Keys of existing permissions.</summary>
    [Required]
    public required IReadOnlyList<string> Permissions { get; init; }
}
