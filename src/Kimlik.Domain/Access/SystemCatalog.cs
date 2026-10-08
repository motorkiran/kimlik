namespace Kimlik.Domain.Access;

/// <summary>Permissions that guard Kimlik itself: its Management API and admin panel, and organization self-service.</summary>
public static class SystemPermissions
{
    public const string UsersRead = "kimlik.users:read";
    public const string UsersWrite = "kimlik.users:write";
    public const string RolesRead = "kimlik.roles:read";
    public const string RolesWrite = "kimlik.roles:write";
    public const string ClientsRead = "kimlik.clients:read";
    public const string ClientsWrite = "kimlik.clients:write";
    public const string AuditRead = "kimlik.audit:read";
    public const string OrganizationsRead = "kimlik.organizations:read";
    public const string OrganizationsWrite = "kimlik.organizations:write";
    public const string PlansRead = "kimlik.plans:read";
    public const string PlansWrite = "kimlik.plans:write";
    public const string SubscriptionsRead = "kimlik.subscriptions:read";
    public const string SubscriptionsWrite = "kimlik.subscriptions:write";

    public const string OrganizationMembersRead = "kimlik.org.members:read";
    public const string OrganizationMembersWrite = "kimlik.org.members:write";
    public const string OrganizationSettingsWrite = "kimlik.org.settings:write";

    /// <summary>The system permissions that apply to the whole installation, held through global roles.</summary>
    public static readonly IReadOnlyDictionary<string, string> Global = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [UsersRead] = "View users, their roles and sessions.",
        [UsersWrite] = "Create, change, suspend and delete users and assign their roles.",
        [RolesRead] = "View permissions and roles.",
        [RolesWrite] = "Define permissions and roles.",
        [ClientsRead] = "View clients and API resources.",
        [ClientsWrite] = "Register and change clients and API resources, and issue client secrets.",
        [AuditRead] = "Read the audit log.",
        [OrganizationsRead] = "View organizations and their members.",
        [OrganizationsWrite] = "Create, change and delete organizations and manage their members.",
        [PlansRead] = "View features and plans.",
        [PlansWrite] = "Define features and plans.",
        [SubscriptionsRead] = "View subscriptions and entitlements.",
        [SubscriptionsWrite] = "Subscribe users and organizations to plans, and change or cancel subscriptions.",
    };

    /// <summary>The system permissions that apply within one organization, held through organization roles.</summary>
    public static readonly IReadOnlyDictionary<string, string> Organization = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [OrganizationMembersRead] = "View the organization's members and invitations.",
        [OrganizationMembersWrite] = "Invite and remove members of the organization and change their roles.",
        [OrganizationSettingsWrite] = "Rename or delete the organization.",
    };

    /// <summary>Every system permission with its description.</summary>
    public static readonly IReadOnlyDictionary<string, string> All = Global.Concat(Organization).ToDictionary(StringComparer.Ordinal);
}

/// <summary>Roles that Kimlik defines.</summary>
public static class SystemRoles
{
    /// <summary>Full control of the installation: every global system permission.</summary>
    public const string Admin = "kimlik-admin";

    /// <summary>Full control of one organization: every organization system permission. Organization creators get it.</summary>
    public const string OrganizationAdmin = "kimlik-org-admin";
}
