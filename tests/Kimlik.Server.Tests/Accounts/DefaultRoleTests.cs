using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Kimlik.Server.Tests.Accounts;

/// <summary>Global roles that every new user gets, from <c>Kimlik:Accounts:DefaultRoles</c>.</summary>
public sealed class DefaultRoleTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PeopleWhoSignUp_AndUsersCreatedThroughTheApi_GetThem()
    {
        var (role, _) = await server.CreateRoleAsync();
        await using var kimlik = WithDefaultRoles(role);
        var email = $"new-{Guid.NewGuid():N}@example.com";

        using var browser = new Browser(kimlik);
        var page = await browser.GetPageAsync("/signup");
        using var signedUp = await browser.SubmitAsync(page, new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = TestUsers.Password,
        });

        using var api = await server.CreateApiClientAsync();
        using var http = kimlik.CreateClient();
        http.DefaultRequestHeaders.Authorization = api.Http.DefaultRequestHeaders.Authorization;
        using var created = await http.PostJsonAsync("/api/v1/users", new CreateUserRequest { Email = $"created-{Guid.NewGuid():N}@example.com" });

        (await RolesOfAsync(email)).ShouldBe([role]);
        (await created.ReadAsync<UserResponse>()).Roles.ShouldBe([role]);
    }

    [Fact]
    public async Task RolesWithSystemPermissions_AreRefused()
    {
        await using var kimlik = WithDefaultRoles(SystemRoles.Admin);
        using var api = await server.CreateApiClientAsync();
        using var http = kimlik.CreateClient();
        http.DefaultRequestHeaders.Authorization = api.Http.DefaultRequestHeaders.Authorization;
        var email = $"created-{Guid.NewGuid():N}@example.com";

        using var refused = await http.PostJsonAsync("/api/v1/users", new CreateUserRequest { Email = email });

        refused.IsSuccessStatusCode.ShouldBeFalse();
        (await server.QueryDatabaseAsync(context => context.Users.AnyAsync(user => user.Email == email, CancellationToken))).ShouldBeFalse();
    }

    private WebApplicationFactory<Program> WithDefaultRoles(string role) =>
        server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kimlik:Accounts:DefaultRoles:0"] = role })));

    private Task<List<string>> RolesOfAsync(string email) =>
        server.QueryDatabaseAsync(context => context.UserRoles
            .Where(assignment => context.Users.Any(user => user.Id == assignment.UserId && user.Email == email))
            .Join(context.Roles, assignment => assignment.RoleId, role => role.Id, (_, role) => role.Key)
            .ToListAsync(CancellationToken));
}
