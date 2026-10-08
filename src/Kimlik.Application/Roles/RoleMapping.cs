using Kimlik.Application.Abstractions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.EntityFrameworkCore;
using DomainRoleScope = Kimlik.Domain.Access.RoleScope;
using RoleScope = Kimlik.Contracts.Management.RoleScope;

namespace Kimlik.Application.Roles;

internal static class RoleMapping
{
    public static RoleResponse ToResponse(this Role role, IEnumerable<string> permissions) => new(
        role.Id,
        role.Key,
        role.Name,
        role.Description,
        role.Scope.ToContract(),
        role.IsSystem,
        [.. permissions.Order(StringComparer.Ordinal)],
        role.CreatedAt,
        role.UpdatedAt);

    public static string[] SortedKeys(this IEnumerable<Permission> permissions) =>
        [.. permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal)];

    public static RoleScope ToContract(this DomainRoleScope scope) => scope == DomainRoleScope.Organization ? RoleScope.Organization : RoleScope.Global;

    public static DomainRoleScope ToDomain(this RoleScope scope) => scope == RoleScope.Organization ? DomainRoleScope.Organization : DomainRoleScope.Global;

    /// <summary>The permission keys of each of the given roles.</summary>
    public static async Task<ILookup<Guid, string>> PermissionsOfAsync(this IKimlikDbContext context, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var links = await context.RolePermissions
            .Where(link => roleIds.Contains(link.RoleId))
            .Join(context.Permissions, link => link.PermissionId, permission => permission.Id, (link, permission) => new { link.RoleId, permission.Key })
            .ToListAsync(cancellationToken);

        return links.ToLookup(link => link.RoleId, link => link.Key);
    }

    public static async Task<RoleResponse> ToResponseAsync(this IKimlikDbContext context, Role role, CancellationToken cancellationToken) =>
        role.ToResponse((await context.PermissionsOfAsync([role.Id], cancellationToken))[role.Id]);
}
