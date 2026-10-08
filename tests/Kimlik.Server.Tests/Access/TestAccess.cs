using Kimlik.Domain.Access;
using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Kimlik.Server.Tests.Access;

/// <summary>Sets up roles and assignments directly in the database, with unique keys per test.</summary>
internal static class TestAccess
{
    /// <summary>Creates a global role with new permissions <c>{resource}:read</c> and <c>{resource}:write</c>.</summary>
    public static Task<(string Role, string[] Permissions)> CreateRoleAsync(this KimlikServerFixture server)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resource = $"invoices{suffix}";
        string[] keys = [$"{resource}:read", $"{resource}:write"];

        return server.QueryDatabaseAsync(async context =>
        {
            var permissions = keys.Select(key => Permission.Create(key, null, DateTimeOffset.UtcNow).Value).ToList();
            var role = Role.Create($"accountant-{suffix}", "Accountant", null, RoleScope.Global, DateTimeOffset.UtcNow).Value;
            role.SetPermissions(permissions, DateTimeOffset.UtcNow);

            context.Permissions.AddRange(permissions);
            context.Roles.Add(role);
            await context.SaveChangesAsync();

            return (role.Key, keys);
        });
    }

    /// <summary>Creates a global role holding existing permissions, such as system permissions.</summary>
    public static Task<string> CreateRoleWithAsync(this KimlikServerFixture server, params string[] permissionKeys) =>
        server.QueryDatabaseAsync(async context =>
        {
            var role = Role.Create($"role-{Guid.NewGuid():N}", "Test role", null, RoleScope.Global, DateTimeOffset.UtcNow).Value;
            role.SetPermissions(await context.Permissions.Where(permission => permissionKeys.Contains(permission.Key)).ToListAsync(), DateTimeOffset.UtcNow);

            context.Roles.Add(role);
            await context.SaveChangesAsync();
            return role.Key;
        });

    public static Task AssignToUserAsync(this KimlikServerFixture server, Guid userId, string roleKey) =>
        server.QueryDatabaseAsync(async context =>
        {
            context.UserRoles.Add(new UserRole(userId, await RoleIdAsync(context, roleKey), DateTimeOffset.UtcNow));
            return await context.SaveChangesAsync();
        });

    public static Task AssignToClientAsync(this KimlikServerFixture server, string clientId, string roleKey) =>
        server.WithServicesAsync(async services =>
        {
            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            var application = await applications.FindByClientIdAsync(clientId) ?? throw new InvalidOperationException($"No client '{clientId}'.");
            var applicationId = Guid.Parse((await applications.GetIdAsync(application))!);

            var context = services.GetRequiredService<KimlikDbContext>();
            context.ClientRoles.Add(new ClientRole(applicationId, await RoleIdAsync(context, roleKey), DateTimeOffset.UtcNow));
            return await context.SaveChangesAsync();
        });

    private static Task<Guid> RoleIdAsync(KimlikDbContext context, string roleKey) =>
        context.Roles.Where(role => role.Key == roleKey).Select(role => role.Id).SingleAsync();
}
