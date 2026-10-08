using Kimlik.Domain.Access;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.Organizations;

internal sealed record TestOrganization(Guid Id, string Slug, string Role, string Permission);

/// <summary>Sets up organizations directly in the database, with unique names per test.</summary>
internal static class TestOrganizations
{
    /// <summary>
    /// Creates an organization with an organization role that holds a new <c>{resource}:read</c> permission, and
    /// makes the given users members with that role.
    /// </summary>
    public static Task<TestOrganization> CreateOrganizationAsync(this KimlikServerFixture server, params Guid[] members) =>
        server.QueryDatabaseAsync(async context =>
        {
            var now = DateTimeOffset.UtcNow;
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var permission = Permission.Create($"projects{suffix}:read", null, now).Value;
            var role = Role.Create($"member-{suffix}", "Member", null, RoleScope.Organization, now).Value;
            role.SetPermissions([permission], now).IsSuccess.ShouldBeTrue();
            var organization = Organization.Create("Acme", $"acme-{suffix}", now).Value;

            context.Permissions.Add(permission);
            context.Roles.Add(role);
            context.Organizations.Add(organization);
            foreach (var userId in members)
            {
                var membership = Membership.Create(organization.Id, userId, now);
                membership.SetRoles([role], now).IsSuccess.ShouldBeTrue();
                context.Memberships.Add(membership);
            }

            await context.SaveChangesAsync();
            return new TestOrganization(organization.Id, organization.Slug, role.Key, permission.Key);
        });

    public static Task RemoveMemberAsync(this KimlikServerFixture server, Guid organizationId, Guid userId) =>
        server.QueryDatabaseAsync(context => context.Memberships
            .Where(membership => membership.OrganizationId == organizationId && membership.UserId == userId)
            .ExecuteDeleteAsync());
}
