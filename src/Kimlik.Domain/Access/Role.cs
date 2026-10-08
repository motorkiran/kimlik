using Kimlik.Domain.Common;

namespace Kimlik.Domain.Access;

public enum RoleScope
{
    /// <summary>Assigned to users and service clients directly; applies everywhere.</summary>
    Global,

    /// <summary>Assigned through an organization membership; applies within that organization only.</summary>
    Organization,
}

/// <summary>A named set of permissions.</summary>
public sealed class Role
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 256;

    private readonly List<RolePermission> _permissions = [];

    // Used by EF Core.
    private Role()
    {
    }

    public Guid Id { get; private init; }

    public string Key { get; private init; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public RoleScope Scope { get; private init; }

    public bool IsSystem { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public static Result<Role> Create(string key, string name, string? description, RoleScope scope, DateTimeOffset now)
    {
        if (!AccessKeys.IsValidRoleKey(key))
        {
            return AccessErrors.InvalidRoleKey;
        }

        if (AccessKeys.IsSystemRole(key))
        {
            return AccessErrors.ReservedKey;
        }

        if (!IsValidName(name))
        {
            return AccessErrors.InvalidName;
        }

        return IsValidDescription(description) ? New(key, name, description, scope, isSystem: false, now) : AccessErrors.InvalidDescription;
    }

    /// <summary>A role defined by Kimlik; see <see cref="SystemRoles"/>.</summary>
    public static Role CreateSystem(string key, string name, string description, RoleScope scope, DateTimeOffset now) =>
        AccessKeys.IsSystemRole(key) && AccessKeys.IsValidRoleKey(key)
            ? New(key, name, description, scope, isSystem: true, now)
            : throw new ArgumentException($"'{key}' is not a system role key.", nameof(key));

    public Result Update(string name, string? description, DateTimeOffset now)
    {
        if (IsSystem)
        {
            return AccessErrors.SystemDefinitionReadOnly;
        }

        if (!IsValidName(name))
        {
            return AccessErrors.InvalidName;
        }

        if (!IsValidDescription(description))
        {
            return AccessErrors.InvalidDescription;
        }

        Name = name.Trim();
        Description = NormalizeDescription(description);
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Replaces the permissions of the role. Organization roles cannot carry access to the whole installation,
    /// and global roles cannot carry access to a single organization.
    /// </summary>
    public Result SetPermissions(IEnumerable<Permission> permissions, DateTimeOffset now)
    {
        if (IsSystem)
        {
            return AccessErrors.SystemDefinitionReadOnly;
        }

        var wanted = permissions.ToList();
        if (!wanted.TrueForAll(permission => AccessKeys.FitsScope(permission.Key, Scope)))
        {
            return Scope == RoleScope.Organization ? AccessErrors.GlobalPermissionInOrganizationRole : AccessErrors.OrganizationPermissionInGlobalRole;
        }

        ReplacePermissions(wanted, now);
        return Result.Success();
    }

    /// <summary>Keeps a system role in sync with the catalog Kimlik ships.</summary>
    public void SyncSystemPermissions(IEnumerable<Permission> permissions, DateTimeOffset now)
    {
        if (!IsSystem)
        {
            throw new InvalidOperationException("Only system roles are synchronized with the catalog.");
        }

        ReplacePermissions(permissions, now);
    }

    private void ReplacePermissions(IEnumerable<Permission> permissions, DateTimeOffset now)
    {
        var wanted = permissions.Select(permission => permission.Id).ToHashSet();
        var current = _permissions.Select(link => link.PermissionId).ToHashSet();
        if (wanted.SetEquals(current))
        {
            return;
        }

        _permissions.RemoveAll(link => !wanted.Contains(link.PermissionId));
        _permissions.AddRange(wanted.Except(current).Select(permissionId => new RolePermission(Id, permissionId)));
        UpdatedAt = now;
    }

    private static Role New(string key, string name, string? description, RoleScope scope, bool isSystem, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Key = key,
        Name = name.Trim(),
        Description = NormalizeDescription(description),
        Scope = scope,
        IsSystem = isSystem,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static bool IsValidName(string? name) => name is not null && name.Trim().Length is > 0 and <= NameMaxLength;

    private static bool IsValidDescription(string? description) => NormalizeDescription(description) is not { Length: > DescriptionMaxLength };

    private static string? NormalizeDescription(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
