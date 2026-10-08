using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Users;

/// <summary>
/// Replaces the global roles of a user. Granting system permissions, or changing the roles of a user who holds
/// them, requires the caller to hold them.
/// </summary>
public sealed class SetUserRolesHandler(IKimlikDbContext context, AccessGuard guard, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<UserResponse>> HandleAsync(Guid userId, SetRolesRequest request, CancellationToken cancellationToken)
    {
        if (await context.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var targetGuard = await guard.EnsureCanManageUserAsync(userId, cancellationToken);
        if (targetGuard.IsFailure)
        {
            return targetGuard.Error;
        }

        var resolution = await RoleSet.ResolveGlobalAsync(context, request.Roles, cancellationToken);
        if (resolution.IsFailure)
        {
            return resolution.Error;
        }

        var wanted = resolution.Value;
        var current = await context.UserRoles.Where(assignment => assignment.UserId == userId).ToListAsync(cancellationToken);

        var added = wanted.Where(role => current.TrueForAll(assignment => assignment.RoleId != role.Id)).ToList();
        var removed = current.Where(assignment => !wanted.Exists(role => role.Id == assignment.RoleId)).ToList();

        var grantGuard = await guard.EnsureCanGrantRolesAsync([.. added.Select(role => role.Id)], cancellationToken);
        if (grantGuard.IsFailure)
        {
            return grantGuard.Error;
        }

        if (added.Count > 0 || removed.Count > 0)
        {
            var now = timeProvider.GetUtcNow();
            context.UserRoles.AddRange(added.Select(role => new UserRole(userId, role.Id, now)));
            context.UserRoles.RemoveRange(removed);
            auditLog.Record(AuditActions.UserRolesChanged, AuditSubject.User(userId), new Dictionary<string, object?>
            {
                ["roles"] = wanted.Select(role => role.Key).Order(StringComparer.Ordinal).ToArray(),
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        return user.ToResponse(wanted.Select(role => role.Key));
    }
}
