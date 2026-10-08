using Kimlik.Application.Abstractions;
using Kimlik.Contracts;
using Kimlik.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Access;

/// <summary>The global roles of a subject and the union of their permissions.</summary>
public sealed record AccessGrant(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)
{
    public static readonly AccessGrant None = new([], []);

    /// <summary>
    /// The grant as it may appear in a token for the given audiences: system permissions only travel to
    /// Kimlik's own API, so an application's resource servers never see them.
    /// </summary>
    public AccessGrant ForAudiences(IEnumerable<string> audiences) =>
        audiences.Contains(KimlikScopes.Api, StringComparer.Ordinal)
            ? this
            : this with { Permissions = [.. Permissions.Where(permission => !AccessKeys.IsSystemPermission(permission))] };
}

/// <summary>Resolves what a user or a service client may do, from its role assignments.</summary>
public sealed class AccessResolver(IKimlikDbContext context)
{
    public Task<AccessGrant> ForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        ResolveAsync(context.UserRoles.Where(assignment => assignment.UserId == userId).Select(assignment => assignment.RoleId), cancellationToken);

    public Task<AccessGrant> ForClientAsync(Guid applicationId, CancellationToken cancellationToken) =>
        ResolveAsync(context.ClientRoles.Where(assignment => assignment.ApplicationId == applicationId).Select(assignment => assignment.RoleId), cancellationToken);

    private async Task<AccessGrant> ResolveAsync(IQueryable<Guid> roleIds, CancellationToken cancellationToken)
    {
        var roles = await context.Roles
            .Where(role => roleIds.Contains(role.Id) && role.Scope == RoleScope.Global)
            .Select(role => role.Key)
            .OrderBy(key => key)
            .ToListAsync(cancellationToken);

        if (roles.Count == 0)
        {
            return AccessGrant.None;
        }

        var permissions = await context.RolePermissions
            .Where(link => roleIds.Contains(link.RoleId))
            .Join(context.Permissions, link => link.PermissionId, permission => permission.Id, (_, permission) => permission.Key)
            .Distinct()
            .OrderBy(key => key)
            .ToListAsync(cancellationToken);

        return new AccessGrant(roles, permissions);
    }
}
