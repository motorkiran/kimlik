using System.Net;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;

namespace Kimlik.Server.Tests.Api;

public sealed class PermissionApiTests(KimlikServerFixture server)
{
    private const string Permissions = "/api/v1/permissions";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Permission_CanBeCreatedDescribedAndDeleted()
    {
        using var api = await server.CreateApiClientAsync();
        var key = NewKey();

        using var created = await api.Http.PostJsonAsync(Permissions, new CreatePermissionRequest { Key = key, Description = "Export reports" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var permission = await created.ReadAsync<PermissionResponse>();
        created.Headers.Location!.OriginalString.ShouldBe($"{Permissions}/{permission.Id}");
        permission.Key.ShouldBe(key);
        permission.IsSystem.ShouldBeFalse();

        using var described = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Permissions}/{permission.Id}", """{ "description": null }""");
        (await described.ReadAsync<PermissionResponse>()).Description.ShouldBeNull();

        using var deleted = await api.Http.DeleteAsync($"{Permissions}/{permission.Id}", CancellationToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var gone = await api.Http.GetAsync($"{Permissions}/{permission.Id}", CancellationToken);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await gone.ReadProblemCodeAsync()).ShouldBe("permission.not_found");
    }

    [Theory]
    [InlineData("kimlik.users:delete", "access.reserved_key")]
    [InlineData("Invoices:Read", "access.invalid_permission_key")]
    [InlineData("invoices", "access.invalid_permission_key")]
    public async Task CreatePermission_WithReservedOrMalformedKey_IsRejected(string key, string code)
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.PostJsonAsync(Permissions, new CreatePermissionRequest { Key = key });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe(code);
    }

    [Fact]
    public async Task CreatePermission_WithTakenKey_IsConflict()
    {
        using var api = await server.CreateApiClientAsync();
        var key = NewKey();
        using var first = await api.Http.PostJsonAsync(Permissions, new CreatePermissionRequest { Key = key });

        using var second = await api.Http.PostJsonAsync(Permissions, new CreatePermissionRequest { Key = key });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadProblemCodeAsync()).ShouldBe("permission.already_exists");
    }

    [Fact]
    public async Task SystemPermissions_AreListed_ButReadOnly()
    {
        using var api = await server.CreateApiClientAsync();

        using var listed = await api.Http.GetAsync($"{Permissions}?q=kimlik.users", CancellationToken);
        var page = await listed.ReadAsync<Page<PermissionResponse>>();
        page.Items.Select(permission => permission.Key).ShouldBe([SystemPermissions.UsersRead, SystemPermissions.UsersWrite, SystemPermissions.UsersImpersonate], ignoreOrder: true);
        page.Items.ShouldAllBe(permission => permission.IsSystem);

        var id = page.Items[0].Id;
        using var described = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Permissions}/{id}", """{ "description": "Mine now" }""");
        using var deleted = await api.Http.DeleteAsync($"{Permissions}/{id}", CancellationToken);

        foreach (var response in new[] { described, deleted })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await response.ReadProblemCodeAsync()).ShouldBe("access.system_definition_read_only");
        }
    }

    [Fact]
    public async Task DeletedPermission_LeavesItsRoles()
    {
        using var api = await server.CreateApiClientAsync();
        var (role, keys) = await server.CreateRoleAsync();
        using var listed = await api.Http.GetAsync($"{Permissions}?q={keys[0]}", CancellationToken);
        var permission = (await listed.ReadAsync<Page<PermissionResponse>>()).Items.ShouldHaveSingleItem();

        using var deleted = await api.Http.DeleteAsync($"{Permissions}/{permission.Id}", CancellationToken);

        using var roles = await api.Http.GetAsync($"/api/v1/roles?q={role}", CancellationToken);
        (await roles.ReadAsync<Page<RoleResponse>>()).Items.ShouldHaveSingleItem().Permissions.ShouldBe([keys[1]]);
    }

    private static string NewKey() => $"reports{Guid.NewGuid():N}:export";
}
