using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Kimlik.Server.Tests.Mfa;
using Kimlik.Server.Tests.Oidc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kimlik.Server.Tests.Accounts;

/// <summary>The hosted account pages, where signed-in users manage their own account.</summary>
public sealed class AccountPageTests(KimlikServerFixture server)
{
    [Theory]
    [InlineData("/account")]
    [InlineData("/account/password")]
    [InlineData("/account/two-factor")]
    [InlineData("/account/sessions")]
    [InlineData("/account/delete")]
    public async Task Pages_AskToSignInFirst(string path)
    {
        using var browser = new Browser(server);

        using var response = await browser.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.PathAndQuery.ShouldBe($"/signin?ReturnUrl={Uri.EscapeDataString(path)}");
    }

    [Fact]
    public async Task Profile_IsSaved()
    {
        var user = await server.CreateUserAsync();
        using var browser = await SignedInAsync(server, user);
        var page = await browser.GetPageAsync("/account");
        page.Text.ShouldContain($"Signed in as {user.Email}.");

        using var unknown = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Locale"] = "xx" });
        (await Browser.ReadPageAsync(unknown)).Text.ShouldContain("Choose a language from the list.");

        using var saved = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.GivenName"] = "Augusta", ["Input.Locale"] = "tr" });
        (await Browser.ReadPageAsync(saved)).Text.ShouldContain("Your profile has been saved.");

        var reloaded = await browser.GetPageAsync("/account");
        reloaded.Document.QuerySelector<IHtmlInputElement>("#Input_GivenName")!.Value.ShouldBe("Augusta");
        reloaded.Document.QuerySelector<IHtmlSelectElement>("#Input_Locale")!.Value.ShouldBe("tr");
    }

    [Fact]
    public async Task ChangingThePassword_KeepsThisBrowser_AndSignsOutTheRest()
    {
        await using var strict = await server.WithStrictSessionsAsync();
        var user = await server.CreateUserAsync();
        using var browser = await SignedInAsync(strict, user);
        using var elsewhere = await SignedInAsync(strict, user);
        var page = await browser.GetPageAsync("/account/password");

        using var wrong = await browser.SubmitAsync(page, Passwords("not the password", "a brand new passphrase"));
        (await Browser.ReadPageAsync(wrong)).Text.ShouldContain("That password is not right.");

        using var changed = await browser.SubmitAsync(page, Passwords(user.Password, "a brand new passphrase"));
        (await Browser.ReadPageAsync(changed)).Text.ShouldContain("Your password has been changed.");

        (await browser.GetAsync("/account")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await elsewhere.GetAsync("/account")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task TwoFactor_IsSetUp_GetsNewRecoveryCodes_AndIsTurnedOff()
    {
        await using var strict = await server.WithStrictSessionsAsync();
        var user = await server.CreateUserAsync();
        using var browser = await SignedInAsync(strict, user);

        var off = await browser.GetPageAsync("/account/two-factor");
        off.Document.QuerySelector("#status")!.TextContent.ShouldStartWith("Off.");

        using var started = await browser.SubmitAsync(off, formSelector: "#set-up");
        var setup = await Browser.ReadPageAsync(started);
        var codes = await TotpCodes.NextThreeAsync(setup.Document.QuerySelector("#secret")!.TextContent.Replace(" ", string.Empty, StringComparison.Ordinal));

        using var wrong = await browser.SubmitAsync(setup, new Dictionary<string, string> { ["Code"] = "000000" }, "#confirm");
        (await Browser.ReadPageAsync(wrong)).Text.ShouldContain("That code is not right.");

        using var confirmed = await browser.SubmitAsync(setup, new Dictionary<string, string> { ["Code"] = codes[0] }, "#confirm");
        var firstCodes = (await Browser.ReadPageAsync(confirmed)).Document.QuerySelectorAll("#recovery-codes code");
        firstCodes.Length.ShouldBe(10);

        var on = await browser.GetPageAsync("/account/two-factor");
        on.Document.QuerySelector("#status")!.TextContent.ShouldStartWith("On.");
        on.Text.ShouldContain("Recovery codes left: 10.");

        using var regenerated = await browser.SubmitAsync(on, new Dictionary<string, string> { ["Code"] = codes[1] }, "#new-recovery-codes");
        var newCodes = (await Browser.ReadPageAsync(regenerated)).Document.QuerySelectorAll("#recovery-codes code").Select(code => code.TextContent);
        newCodes.ShouldNotBe(firstCodes.Select(code => code.TextContent));

        using var withoutCode = await browser.SubmitAsync(on, formSelector: "#turn-off");
        (await Browser.ReadPageAsync(withoutCode)).Text.ShouldContain("Enter the code.");

        using var turnedOff = await browser.SubmitAsync(on, new Dictionary<string, string> { ["Code"] = codes[2] }, "#turn-off");
        var result = await Browser.ReadPageAsync(turnedOff);
        result.Text.ShouldContain("Two-factor authentication is off.");
        result.Document.QuerySelector("#status")!.TextContent.ShouldStartWith("Off.");

        // Each change updated the security stamp, which ends other sessions but not this one.
        (await browser.GetAsync("/account")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Applications_AreListed_AndSignedOut()
    {
        var user = await server.CreateUserAsync();
        var (browser, client, tokens) = await server.SignInAndRedeemAsync(user);
        using var _ = browser;

        var sessions = await browser.GetPageAsync("/account/sessions");
        sessions.Document.QuerySelector("#sessions")!.TextContent.ShouldContain("Orders web app");

        using var revoked = await browser.SubmitAsync(sessions, formSelector: "#sessions form");
        revoked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await browser.GetPageAsync("/account/sessions")).Document.QuerySelector("#no-sessions").ShouldNotBeNull();

        using var refresh = await OidcFlows.RefreshAsync(browser.Client, client, tokens.GetProperty("refresh_token").GetString()!);
        refresh.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var everywhere = await browser.SubmitAsync(sessions, formSelector: "#sign-out-everywhere");
        everywhere.Headers.Location!.OriginalString.ShouldBe("/");
        (await browser.GetAsync("/account")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Account_IsDeleted_WithThePassword()
    {
        var user = await server.CreateUserAsync();
        using var browser = await SignedInAsync(server, user);
        var page = await browser.GetPageAsync("/account/delete");

        using var wrong = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.CurrentPassword"] = "not the password" });
        (await Browser.ReadPageAsync(wrong)).Text.ShouldContain("That password is not right.");

        using var deleted = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.CurrentPassword"] = user.Password });
        (await Browser.ReadPageAsync(deleted)).Text.ShouldContain("Your account has been deleted.");

        (await browser.GetAsync("/account")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using var again = new Browser(server);
        using var signIn = await again.SignInAsync(user.Email, user.Password);
        (await Browser.ReadPageAsync(signIn)).Text.ShouldContain("Invalid email or password.");
    }

    private static Dictionary<string, string> Passwords(string current, string replacement) => new()
    {
        ["Input.CurrentPassword"] = current,
        ["Input.Password"] = replacement,
        ["Input.ConfirmPassword"] = replacement,
    };

    private static async Task<Browser> SignedInAsync(WebApplicationFactory<Program> host, TestUser user)
    {
        var browser = new Browser(host);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        signIn.Headers.Location!.OriginalString.ShouldBe("/");
        return browser;
    }
}
