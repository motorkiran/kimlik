using System.Net;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;

namespace Kimlik.Server.Tests.Passkeys;

/// <summary>Passkeys through the Account API, for the user, and the Management API, for administrators.</summary>
public sealed class PasskeyApiTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_ListsRenamesAndRemovesTheirPasskeys()
    {
        var user = await UserWithPasskeyAsync();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));

        using var listed = await me.GetAsync("/api/v1/me/passkeys", CancellationToken);
        var passkey = (await listed.ReadAsync<List<PasskeyResponse>>()).ShouldHaveSingleItem();
        passkey.Name.ShouldBe("Test passkey");

        using var renamed = await me.SendJsonAsync(HttpMethod.Patch, $"/api/v1/me/passkeys/{passkey.Id}", """{ "name": "Phone" }""");
        (await renamed.ReadAsync<PasskeyResponse>()).Name.ShouldBe("Phone");

        using var removed = await me.DeleteAsync($"/api/v1/me/passkeys/{passkey.Id}", CancellationToken);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var gone = await me.DeleteAsync($"/api/v1/me/passkeys/{passkey.Id}", CancellationToken);
        (await gone.ReadProblemCodeAsync()).ShouldBe("account.passkey_not_found");
    }

    [Fact]
    public async Task Users_CannotTouchSomeoneElsesPasskeys()
    {
        var owner = await UserWithPasskeyAsync();
        var other = await server.CreateUserAsync();
        using var admin = await server.CreateApiClientAsync();
        using var listed = await admin.Http.GetAsync($"/api/v1/users/{owner.Id}/passkeys", CancellationToken);
        var passkey = (await listed.ReadAsync<List<PasskeyResponse>>()).ShouldHaveSingleItem();
        using var me = server.WithToken(await server.UserAccessTokenAsync(other));

        using var removed = await me.DeleteAsync($"/api/v1/me/passkeys/{passkey.Id}", CancellationToken);

        (await removed.ReadProblemCodeAsync()).ShouldBe("account.passkey_not_found");
    }

    [Fact]
    public async Task Administrator_RemovesAUsersPasskey()
    {
        var user = await UserWithPasskeyAsync();
        using var admin = await server.CreateApiClientAsync();
        using var listed = await admin.Http.GetAsync($"/api/v1/users/{user.Id}/passkeys", CancellationToken);
        var passkey = (await listed.ReadAsync<List<PasskeyResponse>>()).ShouldHaveSingleItem();

        using var removed = await admin.Http.DeleteAsync($"/api/v1/users/{user.Id}/passkeys/{passkey.Id}", CancellationToken);

        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var none = await admin.Http.GetAsync($"/api/v1/users/{user.Id}/passkeys", CancellationToken);
        (await none.ReadAsync<List<PasskeyResponse>>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task PasskeysOfAccountsWithMoreAccess_CannotBeRemoved()
    {
        var administrator = await UserWithPasskeyAsync();
        await server.AssignToUserAsync(administrator.Id, SystemRoles.Admin);
        using var support = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.UsersRead, SystemPermissions.UsersWrite));
        using var listed = await support.Http.GetAsync($"/api/v1/users/{administrator.Id}/passkeys", CancellationToken);
        var passkey = (await listed.ReadAsync<List<PasskeyResponse>>()).ShouldHaveSingleItem();

        using var removed = await support.Http.DeleteAsync($"/api/v1/users/{administrator.Id}/passkeys/{passkey.Id}", CancellationToken);

        (await removed.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");
    }

    private async Task<TestUser> UserWithPasskeyAsync()
    {
        var user = await server.CreateUserAsync();
        using var authenticator = new SoftwareAuthenticator();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        await browser.AddPasskeyAsync(authenticator);
        return user;
    }
}
