using Kimlik.Application.Abstractions;
using Kimlik.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Mfa;

/// <summary>Decides who must use a second factor, and who may skip it on a trusted browser.</summary>
public sealed class MfaPolicy(IKimlikDbContext context, IOptions<MfaOptions> options)
{
    public async Task<bool> IsRequiredAsync(Guid userId, CancellationToken cancellationToken) =>
        options.Value.RequireForEveryone || (options.Value.RequireForAdministrators && await IsAdministratorAsync(userId, cancellationToken));

    public async Task<bool> MayRememberBrowserAsync(Guid userId, CancellationToken cancellationToken) =>
        options.Value.RememberBrowserFor > TimeSpan.Zero
        && (options.Value.RememberBrowserForAdministrators || !await IsAdministratorAsync(userId, cancellationToken));

    /// <summary>An administrator holds, through global roles, a system permission for the whole installation.</summary>
    private Task<bool> IsAdministratorAsync(Guid userId, CancellationToken cancellationToken) =>
        context.UserRoles
            .Where(assignment => assignment.UserId == userId)
            .Join(context.RolePermissions, assignment => assignment.RoleId, link => link.RoleId, (_, link) => link.PermissionId)
            .Join(context.Permissions, permissionId => permissionId, permission => permission.Id, (_, permission) => permission)
            .AnyAsync(permission => permission.IsSystem && !permission.Key.StartsWith(AccessKeys.OrganizationSystemPermissionPrefix), cancellationToken);
}
