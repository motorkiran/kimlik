namespace Kimlik.Domain.Auditing;

/// <summary>Audit action names, written as <c>resource.past_tense_verb</c>.</summary>
public static class AuditActions
{
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserSuspended = "user.suspended";
    public const string UserReactivated = "user.reactivated";
    public const string UserDeleted = "user.deleted";
    public const string UserRolesChanged = "user.roles_changed";
    public const string UserSignedIn = "user.signed_in";
    public const string UserSignInFailed = "user.sign_in_failed";
    public const string UserLockedOut = "user.locked_out";
    public const string UserSignedOut = "user.signed_out";
    public const string UserEmailVerified = "user.email_verified";
    public const string UserPasswordResetRequested = "user.password_reset_requested";
    public const string UserPasswordReset = "user.password_reset";
    public const string PermissionCreated = "permission.created";
    public const string PermissionUpdated = "permission.updated";
    public const string PermissionDeleted = "permission.deleted";
    public const string RoleCreated = "role.created";
    public const string RoleUpdated = "role.updated";
    public const string RolePermissionsChanged = "role.permissions_changed";
    public const string RoleDeleted = "role.deleted";
    public const string ApiResourceCreated = "api_resource.created";
    public const string ApiResourceUpdated = "api_resource.updated";
    public const string ApiResourceDeleted = "api_resource.deleted";
    public const string ClientCreated = "client.created";
    public const string ClientUpdated = "client.updated";
    public const string ClientSecretRegenerated = "client.secret_regenerated";
    public const string ClientRolesChanged = "client.roles_changed";
    public const string ClientDeleted = "client.deleted";
    public const string ProvisioningApplied = "provisioning.applied";
    public const string OrganizationCreated = "organization.created";
    public const string OrganizationUpdated = "organization.updated";
    public const string OrganizationDeleted = "organization.deleted";
    public const string MembershipCreated = "membership.created";
    public const string MembershipUpdated = "membership.updated";
    public const string MembershipDeleted = "membership.deleted";
}
