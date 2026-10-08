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

    public static readonly Error InvalidDescription = Error.Validation("access.invalid_description", "A description is at most 256 characters.");
}
