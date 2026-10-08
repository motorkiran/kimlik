using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Accounts;

/// <summary>
/// Gives new users the global roles of <see cref="AccountOptions.DefaultRoles"/>: people who sign up, or sign in with a
/// provider for the first time, and users created through the Management API.
/// </summary>
public sealed class DefaultUserRoles(IKimlikDbContext context, IAuditLog auditLog, IOptions<AccountOptions> options, TimeProvider timeProvider)
{
    /// <summary>Adds the assignments to the changes being saved, and returns the keys of the roles.</summary>
    public async Task<IReadOnlyList<string>> AssignAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (options.Value.DefaultRoles.Count == 0)
        {
            return [];
        }

        // Anyone can become a new user, so the roles must not carry Kimlik's own permissions.
        var roles = await RoleSet.ResolveGlobalAsync(context, options.Value.DefaultRoles, cancellationToken);
        if (roles.IsFailure || await GrantSystemPermissionsAsync([.. roles.Value.Select(role => role.Id)], cancellationToken))
        {
            throw new InvalidOperationException(
                $"{AccountOptions.SectionName}:DefaultRoles must name existing global roles without system permissions.");
        }

        var keys = roles.Value.Select(role => role.Key).Order(StringComparer.Ordinal).ToArray();
        context.UserRoles.AddRange(roles.Value.Select(role => new UserRole(userId, role.Id, timeProvider.GetUtcNow())));
        auditLog.Record(AuditActions.UserRolesChanged, AuditSubject.User(userId), new Dictionary<string, object?> { ["roles"] = keys }, AuditActor.System);
        return keys;
    }

    private Task<bool> GrantSystemPermissionsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken) =>
        context.RolePermissions
            .Where(link => roleIds.Contains(link.RoleId))
            .Join(context.Permissions, link => link.PermissionId, permission => permission.Id, (_, permission) => permission)
            .AnyAsync(permission => permission.IsSystem, cancellationToken);
}
