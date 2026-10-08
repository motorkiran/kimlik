using Kimlik.Application.Abstractions;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Access;

internal static class RoleSet
{
    /// <summary>Loads the roles with the given keys, failing when one is unknown or not a global role.</summary>
    public static Task<Result<List<Role>>> ResolveGlobalAsync(IKimlikDbContext context, IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        ResolveAsync(context, keys, RoleScope.Global, AccessErrors.OrganizationRoleNotAssignable, cancellationToken);

    /// <summary>Loads the roles with the given keys, failing when one is unknown or not an organization role.</summary>
    public static Task<Result<List<Role>>> ResolveOrganizationAsync(IKimlikDbContext context, IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        ResolveAsync(context, keys, RoleScope.Organization, OrganizationErrors.GlobalRoleNotAssignable, cancellationToken);

    private static async Task<Result<List<Role>>> ResolveAsync(
        IKimlikDbContext context, IReadOnlyCollection<string> keys, RoleScope scope, Error wrongScope, CancellationToken cancellationToken)
    {
        var distinctKeys = keys.Distinct(StringComparer.Ordinal).ToList();
        var roles = await context.Roles.Where(role => distinctKeys.Contains(role.Key)).ToListAsync(cancellationToken);

        if (roles.Count != distinctKeys.Count)
        {
            return AccessErrors.UnknownRole;
        }

        return roles.TrueForAll(role => role.Scope == scope) ? roles : wrongScope;
    }
}
