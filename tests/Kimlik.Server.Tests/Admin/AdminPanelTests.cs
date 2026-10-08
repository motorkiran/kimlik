using System.Net;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Microsoft.Extensions.Configuration;

namespace Kimlik.Server.Tests.Admin;

/// <summary>Who gets into the admin panel, and what its pages render before the circuit takes over.</summary>
public sealed class AdminPanelTests(KimlikServerFixture server)
{
    [Fact]
    public async Task Panel_SendsStrangersToSignIn()
    {
        using var browser = new Browser(server);

        using var response = await browser.GetAsync("/admin");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.PathAndQuery.ShouldBe("/signin?ReturnUrl=%2Fadmin");
    }

    [Fact]
    public async Task Panel_TurnsAwayUsersWithoutAccessToKimlik()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var _ = await browser.SignInAsync(user.Email, user.Password);

        using var response = await browser.GetAsync("/admin");

        response.Headers.Location!.OriginalString.ShouldBe("/admin/denied");
        (await browser.GetPageAsync("/admin/denied")).Text.ShouldContain("Your account cannot use the admin panel.");
    }

    [Fact]
    public async Task Administrator_SeesTheDashboard()
    {
        var admin = await server.CreateUserAsync();
        await server.AssignToUserAsync(admin.Id, SystemRoles.Admin);
        using var browser = new Browser(server);
        using var _ = await browser.SignInAsync(admin.Email, admin.Password);

        using var response = await browser.GetAsync("/admin");
        var page = await Browser.ReadPageAsync(response);

        page.Document.QuerySelector("h1")!.TextContent.ShouldBe("Dashboard");
        page.Document.QuerySelector("[data-tile='Users']").ShouldNotBeNull();
        page.Document.QuerySelector("#admin-email")!.TextContent.ShouldBe(admin.Email);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("script-src 'self'");
    }

    [Fact]
    public async Task AdministratorWithoutASecondFactor_SetsOneUpFirst_WhenThePolicyRequiresIt()
    {
        await using var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kimlik:Mfa:RequireForAdministrators"] = "true" })));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);

        // Made an administrator after signing in with a password alone.
        var admin = await server.CreateUserAsync();
        using var browser = new Browser(kimlik);
        using var _ = await browser.SignInAsync(admin.Email, admin.Password);
        await server.AssignToUserAsync(admin.Id, SystemRoles.Admin);

        using var panel = await browser.GetAsync("/admin");
        panel.Headers.Location!.OriginalString.ShouldBe("/admin/step-up?returnUrl=%2Fadmin");

        using var stepUp = await browser.GetAsync(panel.Headers.Location!.OriginalString);
        stepUp.Headers.Location!.OriginalString.ShouldStartWith("/signin/set-up-two-factor?returnUrl=%2Fadmin");
    }

    [Fact]
    public async Task Panel_CanBeTurnedOff()
    {
        await using var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kimlik:Admin:Enabled"] = "false" })));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        using var browser = new Browser(kimlik);

        using var response = await browser.GetAsync("/admin");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
