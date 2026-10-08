using Kimlik.Domain.Common;

namespace Kimlik.Domain.Access;

/// <summary>An atomic capability, such as <c>invoices:read</c>. Applications define their own; Kimlik defines <c>kimlik.*</c>.</summary>
public sealed class Permission
{
    public const int DescriptionMaxLength = 256;

    // Used by EF Core.
    private Permission()
    {
    }

    public Guid Id { get; private init; }

    public string Key { get; private init; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsSystem { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public static Result<Permission> Create(string key, string? description, DateTimeOffset now)
    {
        if (!AccessKeys.IsValidPermissionKey(key))
        {
            return AccessErrors.InvalidPermissionKey;
        }

        if (AccessKeys.IsSystemPermission(key))
        {
            return AccessErrors.ReservedKey;
        }

        return IsValidDescription(description) ? New(key, description, isSystem: false, now) : AccessErrors.InvalidDescription;
    }

    /// <summary>A permission guarding Kimlik itself; see <see cref="SystemPermissions"/>.</summary>
    public static Permission CreateSystem(string key, string description, DateTimeOffset now) =>
        AccessKeys.IsSystemPermission(key) && AccessKeys.IsValidPermissionKey(key)
            ? New(key, description, isSystem: true, now)
            : throw new ArgumentException($"'{key}' is not a system permission key.", nameof(key));

    public Result Describe(string? description)
    {
        if (IsSystem)
        {
            return AccessErrors.SystemDefinitionReadOnly;
        }

        if (!IsValidDescription(description))
        {
            return AccessErrors.InvalidDescription;
        }

        Description = Normalize(description);
        return Result.Success();
    }

    private static Permission New(string key, string? description, bool isSystem, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Key = key,
        Description = Normalize(description),
        IsSystem = isSystem,
        CreatedAt = now,
    };

    private static bool IsValidDescription(string? description) => Normalize(description) is not { Length: > DescriptionMaxLength };

    private static string? Normalize(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
