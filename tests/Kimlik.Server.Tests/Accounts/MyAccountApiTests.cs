using System.Net;
using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;

namespace Kimlik.Server.Tests.Accounts;

/// <summary>The account self-service of the Account API, called with the user's own token.</summary>
public sealed class MyAccountApiTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Profile_ChangesWhatIsSent()
    {
        var user = await server.CreateUserAsync();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));

        using var updated = await me.SendJsonAsync(HttpMethod.Patch, "/api/v1/me", """{ "givenName": "Augusta", "locale": "tr" }""");

        var profile = await updated.ReadAsync<UserResponse>();
        profile.Name.ShouldBe("Augusta Lovelace");
        profile.Locale.ShouldBe("tr");
    }

    [Fact]
    public async Task ChangingThePassword_EndsEverySession()
    {
        var user = await server.CreateUserAsync();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));

        using var wrong = await me.PostJsonAsync("/api/v1/me/password", new ChangePasswordRequest { CurrentPassword = "not the password", NewPassword = "a brand new passphrase" });
        (await wrong.ReadProblemCodeAsync()).ShouldBe("account.wrong_password");

        using var changed = await me.PostJsonAsync("/api/v1/me/password", new ChangePasswordRequest { CurrentPassword = user.Password, NewPassword = "a brand new passphrase" });
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var after = await me.GetAsync("/api/v1/me", CancellationToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, "a brand new passphrase");
        signIn.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task Sessions_CanBeListedAndRevoked()
    {
        var user = await server.CreateUserAsync();
        var (browser, client, tokens) = await server.SignInAndRedeemAsync(user);
        browser.Dispose();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));

        using var listed = await me.GetAsync("/api/v1/me/sessions", CancellationToken);
        var sessions = await listed.ReadAsync<List<SessionResponse>>();
        var orders = sessions.Single(session => session.ClientId == client.ClientId);
        orders.ClientName.ShouldBe("Orders web app");

        using var revoked = await me.DeleteAsync($"/api/v1/me/sessions/{orders.Id}", CancellationToken);
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var http = server.CreateClient();
        using var refresh = await OidcFlows.RefreshAsync(http, client, tokens.GetProperty("refresh_token").GetString()!);
        refresh.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var everywhere = await me.DeleteAsync("/api/v1/me/sessions", CancellationToken);
        everywhere.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var after = await me.GetAsync("/api/v1/me", CancellationToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Account_IsDeleted_WithThePassword()
    {
        var user = await server.CreateUserAsync();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));

        using var wrong = await me.PostJsonAsync("/api/v1/me/delete", new DeleteAccountRequest { Password = "not the password" });
        (await wrong.ReadProblemCodeAsync()).ShouldBe("account.wrong_password");

        using var deleted = await me.PostJsonAsync("/api/v1/me/delete", new DeleteAccountRequest { Password = user.Password });
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var api = await server.CreateApiClientAsync();
        using var gone = await api.Http.GetAsync($"/api/v1/users/{user.Id}", CancellationToken);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
