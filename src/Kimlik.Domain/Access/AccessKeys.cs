using System.Text.RegularExpressions;

namespace Kimlik.Domain.Access;

/// <summary>Formats of the stable keys that identify permissions and roles in tokens and APIs.</summary>
public static partial class AccessKeys
{
    public const int PermissionKeyMaxLength = 128;
    public const int RoleKeyMaxLength = 64;

    /// <summary>Permissions in this namespace guard Kimlik itself and are defined only by Kimlik.</summary>
    public const string SystemPermissionPrefix = "kimlik.";

    /// <summary>Roles with this prefix are defined only by Kimlik.</summary>
    public const string SystemRolePrefix = "kimlik-";

    /// <summary><c>resource:action</c>, where the resource may be dotted, such as <c>projects.members:invite</c>.</summary>
    public static bool IsValidPermissionKey(string? key) =>
        key is { Length: > 0 and <= PermissionKeyMaxLength } && PermissionKeyPattern().IsMatch(key);

    public static bool IsValidRoleKey(string? key) =>
        key is { Length: > 0 and <= RoleKeyMaxLength } && RoleKeyPattern().IsMatch(key);

    public static bool IsSystemPermission(string key) => key.StartsWith(SystemPermissionPrefix, StringComparison.Ordinal);

    public static bool IsSystemRole(string key) => key.StartsWith(SystemRolePrefix, StringComparison.Ordinal);

    [GeneratedRegex("^[a-z][a-z0-9_-]*(\\.[a-z][a-z0-9_-]*)*:[a-z][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionKeyPattern();

    [GeneratedRegex("^[a-z][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleKeyPattern();
}
