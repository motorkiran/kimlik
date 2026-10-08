using Kimlik.Domain.Common;

namespace Kimlik.Domain.Access;

public static class AccessErrors
{
    public static readonly Error InvalidPermissionKey = Error.Validation(
        "access.invalid_permission_key", "Permission keys look like 'resource:action', in lowercase, such as 'invoices:read'.");

    public static readonly Error InvalidRoleKey = Error.Validation(
        "access.invalid_role_key", "Role keys are lowercase letters, digits, '-' and '_', starting with a letter.");

    public static readonly Error ReservedKey = Error.Validation(
        "access.reserved_key", "Keys starting with 'kimlik.' (permissions) or 'kimlik-' (roles) are reserved for Kimlik.");

    public static readonly Error SystemDefinitionReadOnly = Error.Forbidden(
        "access.system_definition_read_only", "Permissions and roles defined by Kimlik cannot be changed or deleted.");

    public static readonly Error InvalidName = Error.Validation("access.invalid_name", "A name is required and is at most 100 characters.");

    public static readonly Error UnknownRole = Error.Validation("access.unknown_role", "One or more roles do not exist.");

    public static readonly Error UnknownPermission = Error.Validation("access.unknown_permission", "One or more permissions do not exist.");

    public static readonly Error OrganizationRoleNotAssignable = Error.Validation(
        "access.organization_role_not_assignable", "Organization roles are assigned through organization memberships.");

    /// <summary>
    /// Nobody can hand out access to Kimlik itself that they do not have, or act on an account that has more of it.
    /// </summary>
    public static readonly Error PrivilegeEscalation = Error.Forbidden(
        "access.privilege_escalation", "Granting system permissions, or managing an account that holds them, requires holding them yourself.");

    public static readonly Error InvalidDescription = Error.Validation("access.invalid_description", "A description is at most 256 characters.");
}
