using System.Linq.Expressions;
using Kimlik.Application.Abstractions;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Organizations;

/// <summary>
/// Authorizes organization self-service from the caller's current roles in the organization, not from a token,
/// so it applies to any organization the user belongs to and reflects changes at once. Within an organization,
/// members only hand out the <c>kimlik.org.*</c> permissions they hold, and only manage members who hold no more.
/// </summary>
public sealed class OrganizationGuard(IKimlikDbContext context)
{
    /// <summary>
    /// The caller's permissions in the organization, once it is checked that they hold <paramref name="permission"/>.
    /// Non-members get "not found", so they learn nothing about the organization.
    /// </summary>
    public async Task<Result<HashSet<string>>> AuthorizeAsync(Guid userId, Guid organizationId, string permission, CancellationToken cancellationToken)
    {
        if (!await context.Memberships.AnyAsync(membership => membership.OrganizationId == organizationId && membership.UserId == userId, cancellationToken))
        {
            return OrganizationErrors.NotFound;
        }

        var permissions = await PermissionsOfMemberAsync(organizationId, userId, cancellationToken);
        return permissions.Contains(permission) ? permissions : OrganizationErrors.MissingPermission;
    }

    /// <summary>Fails when the roles carry an organization system permission the caller does not hold.</summary>
    public async Task<Result> EnsureCanGrantAsync(IReadOnlySet<string> callerPermissions, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken) =>
        Covers(callerPermissions, await SystemPermissionsAsync(link => roleIds.Contains(link.RoleId), cancellationToken));

    /// <summary>Fails when the member holds an organization system permission the caller does not.</summary>
    public async Task<Result> EnsureCanManageAsync(IReadOnlySet<string> callerPermissions, Guid organizationId, Guid memberId, CancellationToken cancellationToken) =>
        Covers(callerPermissions, (await PermissionsOfMemberAsync(organizationId, memberId, cancellationToken)).Where(AccessKeys.IsOrganizationSystemPermission));

    /// <summary>The permissions of the member's roles in the organization.</summary>
    internal async Task<HashSet<string>> PermissionsOfMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        [.. await context.MembershipRoles
            .Where(link => context.Memberships.Any(membership =>
                membership.Id == link.MembershipId && membership.OrganizationId == organizationId && membership.UserId == userId))
            .Join(context.RolePermissions, link => link.RoleId, rolePermission => rolePermission.RoleId, (_, rolePermission) => rolePermission.PermissionId)
            .Join(context.Permissions, permissionId => permissionId, permission => permission.Id, (_, permission) => permission.Key)
            .Distinct()
            .ToListAsync(cancellationToken)];

    private async Task<List<string>> SystemPermissionsAsync(Expression<Func<RolePermission, bool>> roles, CancellationToken cancellationToken) =>
        await context.RolePermissions
            .Where(roles)
            .Join(context.Permissions, link => link.PermissionId, permission => permission.Id, (_, permission) => permission.Key)
            .Where(key => key.StartsWith(AccessKeys.OrganizationSystemPermissionPrefix))
            .Distinct()
            .ToListAsync(cancellationToken);

    private static Result Covers(IReadOnlySet<string> callerPermissions, IEnumerable<string> required) =>
        required.All(callerPermissions.Contains) ? Result.Success() : OrganizationErrors.PrivilegeEscalation;
}
