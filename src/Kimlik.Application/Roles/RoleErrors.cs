using Kimlik.Domain.Common;

namespace Kimlik.Application.Roles;

public static class RoleErrors
{
    public static readonly Error NotFound = Error.NotFound("role.not_found", "The role does not exist.");

    public static readonly Error AlreadyExists = Error.Conflict("role.already_exists", "A role with this key already exists.");
}
