using System.Net;
using Kimlik.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.Accounts;

public sealed class SignUpTests(KimlikServerFixture server)
{
    [Fact]
    public async Task NewAccount_IsCreatedUnverified_AndAudited()
    {
        var email = $"new-{Guid.NewGuid():N}@example.com";

        using var response = await SignUpAsync(email, TestUsers.Password);

        response.Headers.Location!.ToString().ShouldBe("/signup/check-email");
        var user = await server.QueryDatabaseAsync(context => context.Users.SingleAsync(candidate => candidate.Email == email, TestContext.Current.CancellationToken));
        user.EmailConfirmed.ShouldBeFalse();
        user.GivenName.ShouldBe("Grace");
        user.Locale.ShouldBe("en");

        var actions = await server.QueryDatabaseAsync(context => context.AuditEvents
            .Where(auditEvent => auditEvent.SubjectId == user.Id.ToString())
            .Select(auditEvent => auditEvent.Action)
            .ToListAsync(TestContext.Current.CancellationToken));
        actions.ShouldBe([AuditActions.UserCreated]);
    }

    [Fact]
    public async Task TakenEmail_GetsTheSameAnswer_AsANewOne()
    {
        var existing = await server.CreateUserAsync();

        using var response = await SignUpAsync(existing.Email, TestUsers.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe("/signup/check-email");
    }

    [Fact]
    public async Task ShortPassword_IsRejected_WithThePolicyLength()
    {
        using var browser = new Browser(server);
        using var response = await SignUpAsync($"short-{Guid.NewGuid():N}@example.com", "too short", browser);

        var page = await Browser.ReadPageAsync(response);
        page.Text.ShouldContain("Use at least 12 characters.");
    }

    private async Task<HttpResponseMessage> SignUpAsync(string email, string password, Browser? browser = null)
    {
        using var ownBrowser = browser is null ? new Browser(server) : null;
        var activeBrowser = browser ?? ownBrowser!;

        var page = await activeBrowser.GetPageAsync("/signup");
        return await activeBrowser.SubmitAsync(page, new Dictionary<string, string>
        {
            ["Input.GivenName"] = "Grace",
            ["Input.FamilyName"] = "Hopper",
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });
    }
}
