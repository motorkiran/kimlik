using System.Net;
using Kimlik.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.Accounts;

public sealed class SignInTests(KimlikServerFixture server)
{
    [Fact]
    public async Task CorrectPassword_SignsIn_AndIsAudited()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);

        using var response = await browser.SignInAsync(user.Email, user.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe("/");
        (await AuditActionsForAsync(user)).ShouldContain(AuditActions.UserSignedIn);
        (await server.QueryDatabaseAsync(context => context.Users.SingleAsync(candidate => candidate.Id == user.Id, TestContext.Current.CancellationToken)))
            .LastSignInAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WrongPassword_ShowsGenericError_AndIsAudited()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);

        var page = await Browser.ReadPageAsync(await browser.SignInAsync(user.Email, "not the password at all"));

        page.Text.ShouldContain("Invalid email or password.");
        (await AuditActionsForAsync(user)).ShouldContain(AuditActions.UserSignInFailed);
    }

    [Fact]
    public async Task UnknownEmail_ShowsTheSameErrorAsWrongPassword()
    {
        using var browser = new Browser(server);

        var page = await Browser.ReadPageAsync(await browser.SignInAsync($"nobody-{Guid.NewGuid():N}@example.com", TestUsers.Password));

        page.Text.ShouldContain("Invalid email or password.");
    }

    [Fact]
    public async Task RepeatedFailures_LockTheAccount_EvenForTheRightPassword()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await browser.SignInAsync(user.Email, "not the password at all");
        }

        var page = await Browser.ReadPageAsync(await browser.SignInAsync(user.Email, user.Password));
        page.Text.ShouldContain("Too many failed attempts.");
    }

    [Fact]
    public async Task UnverifiedEmail_CannotSignIn()
    {
        var user = await server.CreateUserAsync(emailConfirmed: false);
        using var browser = new Browser(server);

        var page = await Browser.ReadPageAsync(await browser.SignInAsync(user.Email, user.Password));

        page.Text.ShouldContain("Confirm your email address before signing in.");
    }

    [Fact]
    public async Task ReturnUrl_ToAnotherSite_IsIgnored()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);

        using var response = await browser.SignInAsync(user.Email, user.Password, returnUrl: "https://attacker.example/");

        response.Headers.Location!.ToString().ShouldBe("/");
    }

    private Task<List<string>> AuditActionsForAsync(TestUser user) =>
        server.QueryDatabaseAsync(context => context.AuditEvents
            .Where(auditEvent => auditEvent.SubjectId == user.Id.ToString())
            .Select(auditEvent => auditEvent.Action)
            .ToListAsync(TestContext.Current.CancellationToken));
}
