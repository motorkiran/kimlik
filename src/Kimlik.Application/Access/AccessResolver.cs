using Kimlik.Application.Abstractions;
using Kimlik.Contracts;
using Kimlik.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Access;

/// <summary>The roles a user holds in the organization a token acts in.</summary>
public sealed record OrganizationAccess(Guid OrganizationId, IReadOnlyList<string> Roles);

/// <summary>
/// The global roles of a subject, its roles in an organization context if there is one, and the union of
/// their permissions.
/// </summary>
public sealed record AccessGrant(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, OrganizationAccess? Organization = null)
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
    /// <summary>The user's access, including their roles in <paramref name="organizationId"/> when given.</summary>
    public async Task<AccessGrant> ForUserAsync(Guid userId, Guid? organizationId, CancellationToken cancellationToken)
    {
        var globalRoleIds = context.UserRoles.Where(assignment => assignment.UserId == userId).Select(assignment => assignment.RoleId);
        if (organizationId is not { } organization)
        {
            return await ResolveAsync(globalRoleIds, organization: null, cancellationToken);
        }

        var organizationRoleIds = context.MembershipRoles
            .Where(link => context.Memberships.Any(membership =>
                membership.Id == link.MembershipId && membership.UserId == userId && membership.OrganizationId == organization))
            .Select(link => link.RoleId);

        return await ResolveAsync(globalRoleIds, (organization, organizationRoleIds), cancellationToken);
    }

    public Task<AccessGrant> ForClientAsync(Guid applicationId, CancellationToken cancellationToken) =>
        ResolveAsync(
            context.ClientRoles.Where(assignment => assignment.ApplicationId == applicationId).Select(assignment => assignment.RoleId),
            organization: null,
            cancellationToken);

    private async Task<AccessGrant> ResolveAsync(
        IQueryable<Guid> globalRoleIds, (Guid Id, IQueryable<Guid> RoleIds)? organization, CancellationToken cancellationToken)
    {
        var roles = await RoleKeysAsync(globalRoleIds, RoleScope.Global, cancellationToken);
        var permissions = (await PermissionKeysAsync(globalRoleIds, RoleScope.Global, cancellationToken)).ToHashSet(StringComparer.Ordinal);

        OrganizationAccess? organizationAccess = null;
        if (organization is { } scoped)
        {
            organizationAccess = new OrganizationAccess(scoped.Id, await RoleKeysAsync(scoped.RoleIds, RoleScope.Organization, cancellationToken));
            permissions.UnionWith(await PermissionKeysAsync(scoped.RoleIds, RoleScope.Organization, cancellationToken));
        }

        return new AccessGrant(roles, [.. permissions.Order(StringComparer.Ordinal)], organizationAccess);
    }

    private Task<List<string>> RoleKeysAsync(IQueryable<Guid> roleIds, RoleScope scope, CancellationToken cancellationToken) =>
        context.Roles
            .Where(role => roleIds.Contains(role.Id) && role.Scope == scope)
            .Select(role => role.Key)
            .OrderBy(key => key)
            .ToListAsync(cancellationToken);

    /// <summary>Only permissions that fit the scope count, even if one slipped into a role of the other scope.</summary>
    private async Task<IEnumerable<string>> PermissionKeysAsync(IQueryable<Guid> roleIds, RoleScope scope, CancellationToken cancellationToken)
    {
        var keys = await context.RolePermissions
            .Where(link => roleIds.Contains(link.RoleId) && context.Roles.Any(role => role.Id == link.RoleId && role.Scope == scope))
            .Join(context.Permissions, link => link.PermissionId, permission => permission.Id, (_, permission) => permission.Key)
            .Distinct()
            .ToListAsync(cancellationToken);

        return keys.Where(key => AccessKeys.FitsScope(key, scope));
    }
}
