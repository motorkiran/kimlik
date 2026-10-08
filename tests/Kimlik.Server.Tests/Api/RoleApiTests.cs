using System.Net;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using RoleScope = Kimlik.Contracts.Management.RoleScope;

namespace Kimlik.Server.Tests.Api;

public sealed class RoleApiTests(KimlikServerFixture server)
{
    private const string Roles = "/api/v1/roles";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Role_CanBeCreatedChangedAndDeleted_AndLeavesItsHolders()
    {
        using var api = await server.CreateApiClientAsync();
        var (_, permissions) = await server.CreateRoleAsync();
        var key = NewKey();

        using var created = await api.Http.PostJsonAsync(Roles, new CreateRoleRequest { Key = key, Name = "Billing", Permissions = [permissions[0]] });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var role = await created.ReadAsync<RoleResponse>();
        created.Headers.Location!.OriginalString.ShouldBe($"{Roles}/{role.Id}");
        role.Scope.ShouldBe(RoleScope.Global);
        role.Permissions.ShouldBe([permissions[0]]);

        using var renamed = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Roles}/{role.Id}", """{ "name": "Billing team", "description": "Sends invoices" }""");
        var updated = await renamed.ReadAsync<RoleResponse>();
        updated.Name.ShouldBe("Billing team");
        updated.Description.ShouldBe("Sends invoices");
        updated.Permissions.ShouldBe([permissions[0]]);

        using var replaced = await api.Http.PutJsonAsync($"{Roles}/{role.Id}/permissions", new SetPermissionsRequest { Permissions = permissions });
        (await replaced.ReadAsync<RoleResponse>()).Permissions.ShouldBe(permissions, ignoreOrder: true);

        var user = await server.CreateUserAsync();
        using var assigned = await api.Http.PutJsonAsync($"/api/v1/users/{user.Id}/roles", new SetRolesRequest { Roles = [key] });
        assigned.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var deleted = await api.Http.DeleteAsync($"{Roles}/{role.Id}", CancellationToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var holder = await api.Http.GetAsync($"/api/v1/users/{user.Id}", CancellationToken);
        (await holder.ReadAsync<UserResponse>()).Roles.ShouldBeEmpty();
        using var gone = await api.Http.GetAsync($"{Roles}/{role.Id}", CancellationToken);
        (await gone.ReadProblemCodeAsync()).ShouldBe("role.not_found");
    }

    [Fact]
    public async Task UpdateRole_CannotClearTheName()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync(Roles, new CreateRoleRequest { Key = NewKey(), Name = "Billing" });
        var role = await created.ReadAsync<RoleResponse>();

        using var response = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Roles}/{role.Id}", """{ "name": null }""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe("access.invalid_name");
    }

    [Theory]
    [InlineData("""{ "key": "kimlik-auditor", "name": "Auditor" }""", "access.reserved_key")]
    [InlineData("""{ "key": "Billing", "name": "Billing" }""", "access.invalid_role_key")]
    [InlineData("""{ "key": "billing-{0}", "name": "Billing", "permissions": ["no.such:permission"] }""", "access.unknown_permission")]
    [InlineData("""{ "key": "billing-{0}", "name": "Billing", "scope": 1 }""", "request.invalid")]
    public async Task CreateRole_WithInvalidRequest_IsRejected(string json, string code)
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.SendJsonAsync(HttpMethod.Post, Roles, json.Replace("{0}", Guid.NewGuid().ToString("N")[..8], StringComparison.Ordinal));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe(code);
    }

    [Fact]
    public async Task CreateRole_WithTakenKey_IsConflict()
    {
        using var api = await server.CreateApiClientAsync();
        var key = NewKey();
        using var first = await api.Http.PostJsonAsync(Roles, new CreateRoleRequest { Key = key, Name = "First" });

        using var second = await api.Http.PostJsonAsync(Roles, new CreateRoleRequest { Key = key, Name = "Second" });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadProblemCodeAsync()).ShouldBe("role.already_exists");
    }

    [Fact]
    public async Task SystemPermissions_InARole_RequireHoldingThem()
    {
        using var roleManager = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.RolesRead, SystemPermissions.RolesWrite));

        using var escalated = await roleManager.Http.PostJsonAsync(Roles, new CreateRoleRequest
        {
            Key = NewKey(),
            Name = "Support",
            Permissions = [SystemPermissions.UsersWrite],
        });
        escalated.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await escalated.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");

        using var held = await roleManager.Http.PostJsonAsync(Roles, new CreateRoleRequest
        {
            Key = NewKey(),
            Name = "Catalog reader",
            Permissions = [SystemPermissions.RolesRead],
        });
        held.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task RoleWithSystemPermissionsTheCallerLacks_CannotBeChanged()
    {
        var support = await server.CreateRoleWithAsync(SystemPermissions.UsersWrite);
        using var api = await server.CreateApiClientAsync();
        using var listed = await api.Http.GetAsync($"{Roles}?q={support}", CancellationToken);
        var supportId = (await listed.ReadAsync<Page<RoleResponse>>()).Items.ShouldHaveSingleItem().Id;
        using var roleManager = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.RolesRead, SystemPermissions.RolesWrite));

        using var renamed = await roleManager.Http.SendJsonAsync(HttpMethod.Patch, $"{Roles}/{supportId}", """{ "name": "Nobody" }""");
        using var stripped = await roleManager.Http.PutJsonAsync($"{Roles}/{supportId}/permissions", new SetPermissionsRequest { Permissions = [] });
        using var deleted = await roleManager.Http.DeleteAsync($"{Roles}/{supportId}", CancellationToken);

        foreach (var response in new[] { renamed, stripped, deleted })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await response.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");
        }
    }

    [Fact]
    public async Task SystemRole_IsReadOnly()
    {
        using var api = await server.CreateApiClientAsync();
        using var listed = await api.Http.GetAsync($"{Roles}?q={SystemRoles.Admin}", CancellationToken);
        var admin = (await listed.ReadAsync<Page<RoleResponse>>()).Items.ShouldHaveSingleItem();
        admin.IsSystem.ShouldBeTrue();
        admin.Permissions.ShouldBe(SystemPermissions.Global.Keys, ignoreOrder: true);

        using var renamed = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Roles}/{admin.Id}", """{ "name": "Root" }""");
        using var stripped = await api.Http.PutJsonAsync($"{Roles}/{admin.Id}/permissions", new SetPermissionsRequest { Permissions = [] });
        using var deleted = await api.Http.DeleteAsync($"{Roles}/{admin.Id}", CancellationToken);

        foreach (var response in new[] { renamed, stripped, deleted })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await response.ReadProblemCodeAsync()).ShouldBe("access.system_definition_read_only");
        }
    }

    [Fact]
    public async Task ListRoles_FiltersByScope()
    {
        using var api = await server.CreateApiClientAsync();
        var marker = $"team{Guid.NewGuid():N}"[..20];
        using var created = await api.Http.PostJsonAsync(Roles, new CreateRoleRequest { Key = $"{marker}-owner", Name = "Owner", Scope = RoleScope.Organization });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var organization = await api.Http.GetAsync($"{Roles}?q={marker}&scope=organization", CancellationToken);
        (await organization.ReadAsync<Page<RoleResponse>>()).Items.ShouldHaveSingleItem().Scope.ShouldBe(RoleScope.Organization);

        using var global = await api.Http.GetAsync($"{Roles}?q={marker}&scope=global", CancellationToken);
        (await global.ReadAsync<Page<RoleResponse>>()).Items.ShouldBeEmpty();

        using var invalid = await api.Http.GetAsync($"{Roles}?scope=team", CancellationToken);
        (await invalid.ReadProblemCodeAsync()).ShouldBe("common.invalid_parameter");
    }

    private static string NewKey() => $"role-{Guid.NewGuid():N}";
}
