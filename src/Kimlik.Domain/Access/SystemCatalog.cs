namespace Kimlik.Domain.Access;

/// <summary>Permissions that guard Kimlik's own Management API and admin panel.</summary>
public static class SystemPermissions
{
    public const string UsersRead = "kimlik.users:read";
    public const string UsersWrite = "kimlik.users:write";
    public const string RolesRead = "kimlik.roles:read";
    public const string RolesWrite = "kimlik.roles:write";
    public const string ClientsRead = "kimlik.clients:read";
    public const string ClientsWrite = "kimlik.clients:write";
    public const string AuditRead = "kimlik.audit:read";

    /// <summary>Every system permission with its description.</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [UsersRead] = "View users, their roles and sessions.",
        [UsersWrite] = "Create, change, suspend and delete users and assign their roles.",
        [RolesRead] = "View permissions and roles.",
        [RolesWrite] = "Define permissions and roles.",
        [ClientsRead] = "View clients and API resources.",
        [ClientsWrite] = "Register and change clients and API resources, and issue client secrets.",
        [AuditRead] = "Read the audit log.",
    };
}

/// <summary>Roles that Kimlik defines.</summary>
public static class SystemRoles
{
    /// <summary>Full control of the installation: every system permission.</summary>
    public const string Admin = "kimlik-admin";
}
