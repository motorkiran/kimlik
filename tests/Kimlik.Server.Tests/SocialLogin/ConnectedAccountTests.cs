using System.Net;
using Kimlik.Application.Accounts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Users;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.SocialLogin;

/// <summary>Connecting and disconnecting accounts at other providers, from the account pages and the APIs.</summary>
public sealed class ConnectedAccountTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SignedInUser_ConnectsAndDisconnectsAnAccount()
    {
        var provider = await server.TestProviderAsync();
        var user = await server.CreateUserAsync();
        var profile = TestProfile.New();
        using var browser = new Browser(provider.Kimlik);
        using var _ = await browser.SignInAsync(user.Email, user.Password);

        var logins = await browser.GetPageAsync("/account/logins");
        Status(logins).ShouldBe("Not connected");

        using var toProvider = await browser.SubmitAsync(logins, formSelector: ProviderForm);
        using var back = await provider.SignInAsync(browser, toProvider, profile);
        back.Headers.Location!.OriginalString.ShouldBe("/account/logins");
        var connected = await browser.GetPageAsync("/account/logins");
        Status(connected).ShouldBe("Connected");

        using var disconnected = await browser.SubmitAsync(connected, formSelector: ProviderForm);
        var result = await Browser.ReadPageAsync(disconnected);
        result.Text.ShouldContain("The account was disconnected.");
        Status(result).ShouldBe("Not connected");
    }

    [Fact]
    public async Task AccountAtTheProvider_BelongsToOneUser()
    {
        var provider = await server.TestProviderAsync();
        var owner = await server.CreateUserAsync();
        var other = await server.CreateUserAsync();
        var profile = TestProfile.New();
        await LinkAsync(owner.Id, profile);
        using var browser = new Browser(provider.Kimlik);
        using var _ = await browser.SignInAsync(other.Email, other.Password);

        using var toProvider = await browser.SubmitAsync(await browser.GetPageAsync("/account/logins"), formSelector: ProviderForm);
        using var refused = await provider.SignInAsync(browser, toProvider, profile);

        (await Browser.ReadPageAsync(refused)).Text.ShouldContain($"That {TestProvider.DisplayName} account is already connected to another account.");
    }

    [Fact]
    public async Task UserWithoutAPassword_KeepsAWayToSignIn()
    {
        var provider = await server.TestProviderAsync();
        var profile = TestProfile.New();
        using var browser = new Browser(provider.Kimlik);
        var signIn = await browser.GetPageAsync("/signin");
        using var toProvider = await browser.SubmitAsync(signIn, formSelector: "form[action^='/signin/external']");
        using var signedUp = await provider.SignInAsync(browser, toProvider, profile);

        var logins = await browser.GetPageAsync("/account/logins");
        using var refused = await browser.SubmitAsync(logins, formSelector: ProviderForm);
        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("Set a password or connect another account first");

        var password = await browser.GetPageAsync("/account/password");
        password.Text.ShouldContain("Set a password");
        using var set = await browser.SubmitAsync(password, new Dictionary<string, string>
        {
            ["Input.Password"] = "a password of my own",
            ["Input.ConfirmPassword"] = "a password of my own",
        });
        (await Browser.ReadPageAsync(set)).Text.ShouldContain("Your password has been set.");

        using var disconnected = await browser.SubmitAsync(await browser.GetPageAsync("/account/logins"), formSelector: ProviderForm);
        (await Browser.ReadPageAsync(disconnected)).Text.ShouldContain("The account was disconnected.");

        using var withPassword = new Browser(provider.Kimlik);
        (await withPassword.SignInAsync(profile.Email!, "a password of my own")).Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task UserWithoutAPassword_DeletesTheAccountByTypingTheAddress()
    {
        var provider = await server.TestProviderAsync();
        var profile = TestProfile.New();
        using var browser = new Browser(provider.Kimlik);
        var signIn = await browser.GetPageAsync("/signin");
        using var toProvider = await browser.SubmitAsync(signIn, formSelector: "form[action^='/signin/external']");
        using var signedUp = await provider.SignInAsync(browser, toProvider, profile);
        var page = await browser.GetPageAsync("/account/delete");

        using var wrong = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Email"] = "someone-else@example.com" });
        (await Browser.ReadPageAsync(wrong)).Text.ShouldContain("Type the email address of your account.");

        using var deleted = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Email"] = profile.Email!.ToUpperInvariant() });
        (await Browser.ReadPageAsync(deleted)).Text.ShouldContain("Your account has been deleted.");
        (await server.WithServicesAsync(async services => await services.GetRequiredService<UserManager<User>>().FindByEmailAsync(profile.Email))).ShouldBeNull();
    }

    [Fact]
    public async Task AccountApi_ListsAndDisconnectsLogins()
    {
        var user = await server.CreateUserAsync();
        await LinkAsync(user.Id, TestProfile.New());
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));

        using var listed = await me.GetAsync("/api/v1/me/logins", CancellationToken);
        (await listed.ReadAsync<List<UserLoginResponse>>()).ShouldBe([new UserLoginResponse(TestProvider.Name, TestProvider.DisplayName)]);

        using var missing = await me.DeleteAsync("/api/v1/me/logins/unknown", CancellationToken);
        (await missing.ReadProblemCodeAsync()).ShouldBe("account.login_not_found");

        using var unlinked = await me.DeleteAsync($"/api/v1/me/logins/{TestProvider.Name}", CancellationToken);
        unlinked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ManagementApi_ListsAndDisconnectsLogins_EvenTheLastOne()
    {
        var user = await server.CreateUserAsync();
        await server.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<User>>();
            (await users.RemovePasswordAsync((await users.FindByIdAsync(user.Id.ToString()))!)).Succeeded.ShouldBeTrue();
        });
        await LinkAsync(user.Id, TestProfile.New());
        using var api = await server.CreateApiClientAsync();

        using var listed = await api.Http.GetAsync($"/api/v1/users/{user.Id}/logins", CancellationToken);
        (await listed.ReadAsync<List<UserLoginResponse>>()).Single().Provider.ShouldBe(TestProvider.Name);

        using var unlinked = await api.Http.DeleteAsync($"/api/v1/users/{user.Id}/logins/{TestProvider.Name}", CancellationToken);
        unlinked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var reader = await server.CreateApiClientAsync(role: null);
        using var forbidden = await reader.Http.GetAsync($"/api/v1/users/{user.Id}/logins", CancellationToken);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private const string ProviderForm = $"li[data-provider='{TestProvider.Name}'] form";

    private static string Status(WebPage page) =>
        page.Document.QuerySelector($"li[data-provider='{TestProvider.Name}'] .hint")!.TextContent.Trim();

    private Task LinkAsync(Guid userId, TestProfile profile) =>
        server.WithServicesAsync(async services =>
            (await services.GetRequiredService<ExternalLogins>().LinkAsync(
                userId, new ExternalLogin(TestProvider.Name, profile.Subject, TestProvider.DisplayName), CancellationToken)).IsSuccess.ShouldBeTrue());
}
