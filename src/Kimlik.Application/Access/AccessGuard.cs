using System.Linq.Expressions;
using Kimlik.Application.Abstractions;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Access;

/// <summary>
/// Keeps callers within their own access to Kimlik: they cannot hand out system permissions they do not hold,
/// nor change, suspend or delete an account, a client or a role that holds such permissions. Kimlik itself, such
/// as when it applies configuration at startup, is not limited.
/// </summary>
public sealed class AccessGuard(IKimlikDbContext context, IRequestContext request)
{
    /// <summary>Fails when the roles carry a system permission the caller does not have.</summary>
    public Task<Result> EnsureCanGrantRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken) =>
        EnsureHoldsAsync(link => roleIds.Contains(link.RoleId), cancellationToken);

    /// <summary>Fails when the user has a system permission the caller does not have.</summary>
    public Task<Result> EnsureCanManageUserAsync(Guid userId, CancellationToken cancellationToken) =>
        EnsureHoldsAsync(
            link => context.UserRoles.Any(assignment => assignment.UserId == userId && assignment.RoleId == link.RoleId),
            cancellationToken);

    /// <summary>Fails when the client has a system permission the caller does not have.</summary>
    public Task<Result> EnsureCanManageClientAsync(Guid applicationId, CancellationToken cancellationToken) =>
        EnsureHoldsAsync(
            link => context.ClientRoles.Any(assignment => assignment.ApplicationId == applicationId && assignment.RoleId == link.RoleId),
            cancellationToken);

    /// <summary>Fails when the role carries a system permission the caller does not have.</summary>
    public Task<Result> EnsureCanManageRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        EnsureHoldsAsync(link => link.RoleId == roleId, cancellationToken);

    /// <summary>
    /// Fails when the permissions include a system permission for the whole installation that the caller does not
    /// have. Organization system permissions only apply within an organization, which global access covers.
    /// </summary>
    public Result EnsureCanGrant(IEnumerable<string> permissions) =>
        IsKimlik || permissions.Where(IsInstallationWide).All(request.Permissions.Contains)
            ? Result.Success()
            : AccessErrors.PrivilegeEscalation;

    private static bool IsInstallationWide(string permission) =>
        AccessKeys.IsSystemPermission(permission) && !AccessKeys.IsOrganizationSystemPermission(permission);

    private bool IsKimlik => request.Actor.Type == AuditActorType.System;

    /// <summary>Fails when a system permission linked to the selected roles is missing from the caller.</summary>
    private async Task<Result> EnsureHoldsAsync(Expression<Func<RolePermission, bool>> roles, CancellationToken cancellationToken)
    {
        if (IsKimlik)
        {
            return Result.Success();
        }

        var systemPermissions = await context.RolePermissions
            .Where(roles)
            .Join(context.Permissions, link => link.PermissionId, permission => permission.Id, (_, permission) => permission)
            .Where(permission => permission.IsSystem)
            .Select(permission => permission.Key)
            .Distinct()
            .ToListAsync(cancellationToken);

        return EnsureCanGrant(systemPermissions);
    }
}
