using Kimlik.Application.Abstractions;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Access;

internal static class RoleSet
{
    /// <summary>Loads the roles with the given keys, failing when one is unknown or not a global role.</summary>
    public static async Task<Result<List<Role>>> ResolveGlobalAsync(IKimlikDbContext context, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var distinctKeys = keys.Distinct(StringComparer.Ordinal).ToList();
        var roles = await context.Roles.Where(role => distinctKeys.Contains(role.Key)).ToListAsync(cancellationToken);

        if (roles.Count != distinctKeys.Count)
        {
            return AccessErrors.UnknownRole;
        }

        return roles.TrueForAll(role => role.Scope == RoleScope.Global) ? roles : AccessErrors.OrganizationRoleNotAssignable;
    }
}
