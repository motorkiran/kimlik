using Kimlik.Domain.Common;

namespace Kimlik.Application.Permissions;

public static class PermissionErrors
{
    public static readonly Error NotFound = Error.NotFound("permission.not_found", "The permission does not exist.");

    public static readonly Error AlreadyExists = Error.Conflict("permission.already_exists", "A permission with this key already exists.");
}
