using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;

namespace Kimlik.Application.Permissions;

internal static class PermissionMapping
{
    public static PermissionResponse ToResponse(this Permission permission) =>
        new(permission.Id, permission.Key, permission.Description, permission.IsSystem, permission.CreatedAt);
}
