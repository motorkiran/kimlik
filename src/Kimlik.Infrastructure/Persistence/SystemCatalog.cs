using Kimlik.Contracts;
using Kimlik.Domain.Access;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Kimlik.Infrastructure.Persistence;

/// <summary>
/// Brings the definitions Kimlik ships with up to date: system permissions, the administrator roles of the
/// installation and of organizations, and the scope of Kimlik's own API. Idempotent; runs as part of database preparation, after migrations.
/// </summary>
internal sealed class SystemCatalog(KimlikDbContext context, IOpenIddictScopeManager scopes, TimeProvider timeProvider)
{
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var permissions = await context.Permissions.Where(permission => permission.IsSystem).ToDictionaryAsync(permission => permission.Key, cancellationToken);
        foreach (var (key, description) in SystemPermissions.All)
        {
            if (!permissions.ContainsKey(key))
            {
                permissions[key] = context.Permissions.Add(Permission.CreateSystem(key, description, now)).Entity;
            }
        }

        // Permissions a newer version no longer ships are removed, together with their role links.
        foreach (var obsolete in permissions.Keys.Where(key => !SystemPermissions.All.ContainsKey(key)).ToList())
        {
            context.Permissions.Remove(permissions[obsolete]);
            permissions.Remove(obsolete);
        }

        var admin = await SystemRoleAsync(SystemRoles.Admin, "Kimlik administrator", "Full control of this Kimlik installation.", RoleScope.Global, now, cancellationToken);
        admin.SyncSystemPermissions(permissions.Values.Where(permission => SystemPermissions.Global.ContainsKey(permission.Key)), now);

        var organizationAdmin = await SystemRoleAsync(
            SystemRoles.OrganizationAdmin, "Organization administrator", "Full control of one organization.", RoleScope.Organization, now, cancellationToken);
        organizationAdmin.SyncSystemPermissions(permissions.Values.Where(permission => SystemPermissions.Organization.ContainsKey(permission.Key)), now);

        await context.SaveChangesAsync(cancellationToken);

        if (await scopes.FindByNameAsync(KimlikScopes.Api, cancellationToken) is null)
        {
            await scopes.CreateAsync(
                new OpenIddictScopeDescriptor { Name = KimlikScopes.Api, DisplayName = "Manage this Kimlik installation", Resources = { KimlikScopes.Api } },
                cancellationToken);
        }
    }

    private async Task<Role> SystemRoleAsync(string key, string name, string description, RoleScope scope, DateTimeOffset now, CancellationToken cancellationToken) =>
        await context.Roles.Include(role => role.Permissions).SingleOrDefaultAsync(role => role.Key == key, cancellationToken)
            ?? context.Roles.Add(Role.CreateSystem(key, name, description, scope, now)).Entity;
}
