using Kimlik.Domain.Auditing;
using Kimlik.Server.Tests.Accounts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Passkeys;

/// <summary>Passkeys on the account pages: added through the browser's ceremony, renamed and removed.</summary>
public sealed class AccountPasskeyTests(KimlikServerFixture server)
{
    [Fact]
    public async Task Passkey_IsAdded_Renamed_AndRemoved()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        using var authenticator = new SoftwareAuthenticator();
        var page = await browser.GetPageAsync("/account/passkeys");
        page.Text.ShouldContain("You have no passkeys yet.");

        using var added = await browser.RunPasskeyFormAsync(
            page, "#add-passkey", options => authenticator.Create(options, synced: true), new Dictionary<string, string> { ["Name"] = "My laptop" });
        var withPasskey = await Browser.ReadPageAsync(added);
        withPasskey.Text.ShouldContain("Your passkey has been added.");
        withPasskey.Text.ShouldContain("Synced to your other devices");
        withPasskey.Document.QuerySelector("#passkeys input[name=name]")!.GetAttribute("value").ShouldBe("My laptop");

        using var renamed = await browser.SubmitAsync(withPasskey, new Dictionary<string, string> { ["name"] = "Work laptop" }, "#passkeys form[action*=Rename]");
        var afterRename = await browser.GetPageAsync("/account/passkeys");
        afterRename.Document.QuerySelector("#passkeys input[name=name]")!.GetAttribute("value").ShouldBe("Work laptop");

        using var removed = await browser.SubmitAsync(afterRename, formSelector: "#passkeys form[action*=Remove]");
        (await browser.GetPageAsync("/account/passkeys")).Text.ShouldContain("You have no passkeys yet.");

        var actions = await server.QueryDatabaseAsync(context => context.AuditEvents
            .Where(auditEvent => auditEvent.SubjectId == user.Id.ToString() && auditEvent.Action.StartsWith("user.passkey_"))
            .Select(auditEvent => auditEvent.Action)
            .ToListAsync(TestContext.Current.CancellationToken));
        actions.ShouldBe([AuditActions.UserPasskeyAdded, AuditActions.UserPasskeyRenamed, AuditActions.UserPasskeyRemoved], ignoreOrder: true);
    }

    [Fact]
    public async Task OptionsMadeForSomeoneElse_DoNotAddAPasskey()
    {
        var victim = await server.CreateUserAsync();
        var attacker = await server.CreateUserAsync();
        using var victimBrowser = new Browser(server);
        using var attackerBrowser = new Browser(server);
        using var victimSignIn = await victimBrowser.SignInAsync(victim.Email, victim.Password);
        using var attackerSignIn = await attackerBrowser.SignInAsync(attacker.Email, attacker.Password);
        using var authenticator = new SoftwareAuthenticator();

        // The attacker's own options, answered, then posted into the victim's session.
        var (options, state) = await attackerBrowser.FetchPasskeyOptionsAsync(await attackerBrowser.GetPageAsync("/account/passkeys"), "#add-passkey");
        var victimPage = await victimBrowser.GetPageAsync("/account/passkeys");
        using var refused = await victimBrowser.SubmitAsync(
            victimPage, new Dictionary<string, string> { ["Credential"] = authenticator.Create(options), ["State"] = state }, "#add-passkey");

        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("The passkey could not be added.");
        (await server.QueryDatabaseAsync(context => context.UserPasskeys.CountAsync(passkey => passkey.UserId == victim.Id, TestContext.Current.CancellationToken)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task AddingAPasskey_TakesARecentSignIn()
    {
        var clock = new ShiftedTimeProvider();
        await using var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(kimlik);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        clock.Offset = TimeSpan.FromMinutes(11);
        var page = await browser.GetPageAsync("/account/passkeys");

        page.Document.QuerySelector("#add-passkey").ShouldBeNull();
        page.Text.ShouldContain("For your security, sign in again before you add a passkey.");
    }
}

/// <summary>The system clock, moved by <see cref="Offset"/>, for tests about how long ago something happened.</summary>
internal sealed class ShiftedTimeProvider : TimeProvider
{
    public TimeSpan Offset { get; set; }

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;
}
