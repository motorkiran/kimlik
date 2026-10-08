using Kimlik.Application.Abstractions;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Access;

internal static class PermissionSet
{
    /// <summary>Loads the permissions with the given keys, failing when one is unknown.</summary>
    public static async Task<Result<List<Permission>>> ResolveAsync(IKimlikDbContext context, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var distinctKeys = keys.Distinct(StringComparer.Ordinal).ToList();
        var permissions = await context.Permissions.Where(permission => distinctKeys.Contains(permission.Key)).ToListAsync(cancellationToken);

        return permissions.Count == distinctKeys.Count ? permissions : AccessErrors.UnknownPermission;
    }
}
