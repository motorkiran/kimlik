using System.Net;
using Kimlik.Server.Tests.Oidc;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Tests.Accounts;

public sealed class AccountEmailTests(KimlikServerFixture server)
{
    private const string NewPassword = "a brand new passphrase";

    [Fact]
    public async Task SignUp_SendsVerificationLink_ThatConfirmsTheAddress()
    {
        var email = $"verify-{Guid.NewGuid():N}@example.com";
        using var browser = new Browser(server);
        await SignUpAsync(browser, email);

        var message = await server.Emails.WaitForAsync(email, "Confirm your email address");
        message.HtmlBody.ShouldContain("Hi Grace,");

        var page = await browser.GetPageAsync(CapturingEmailSender.LinkIn(message));
        page.Text.ShouldContain("Your email address is confirmed.");

        using var signIn = await browser.SignInAsync(email, TestUsers.Password);
        signIn.Headers.Location!.ToString().ShouldBe("/signin/passkey?returnUrl=%2F");
    }

    [Fact]
    public async Task VerificationEmail_IsWrittenInTheLanguageOfTheSignUp()
    {
        var email = $"turkish-{Guid.NewGuid():N}@example.com";
        using var browser = new Browser(server, acceptLanguage: "tr");
        await SignUpAsync(browser, email);

        var message = await server.Emails.WaitForAsync(email, "e-posta adresinizi doğrulayın");
        message.TextBody.ShouldContain("Merhaba Grace,");
    }

    [Fact]
    public async Task SignUp_WithTakenAddress_TellsTheOwnerByEmail()
    {
        var owner = await server.CreateUserAsync();
        using var browser = new Browser(server);

        await SignUpAsync(browser, owner.Email);

        await server.Emails.WaitForAsync(owner.Email, "You already have a Kimlik account");
    }

    [Fact]
    public async Task PasswordReset_ChangesThePassword_AndEndsEverySession()
    {
        var user = await server.CreateUserAsync();
        var (signedIn, client, tokens) = await server.SignInAndRedeemAsync(user);
        using var _ = signedIn;

        using var browser = new Browser(server);
        var forgotPage = await browser.GetPageAsync("/forgot-password");
        var sent = await Browser.ReadPageAsync(await browser.SubmitAsync(forgotPage, new Dictionary<string, string> { ["Input.Email"] = user.Email }));
        sent.Text.ShouldContain("If an account exists for that address");

        var resetLink = CapturingEmailSender.LinkIn(await server.Emails.WaitForAsync(user.Email, "Reset your Kimlik password"));
        var resetPage = await browser.GetPageAsync(resetLink);
        var done = await Browser.ReadPageAsync(await browser.SubmitAsync(resetPage, new Dictionary<string, string>
        {
            ["Input.Password"] = NewPassword,
            ["Input.ConfirmPassword"] = NewPassword,
        }));
        done.Text.ShouldContain("Your password has been changed.");

        using var oldPassword = await browser.SignInAsync(user.Email, user.Password);
        (await Browser.ReadPageAsync(oldPassword)).Text.ShouldContain("Invalid email or password.");

        using var newPassword = await new Browser(server).SignInAsync(user.Email, NewPassword);
        newPassword.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        using var refresh = await OidcFlows.RefreshAsync(signedIn.Client, client, tokens.GetProperty("refresh_token").GetString()!);
        (await refresh.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe(Errors.InvalidGrant);

        // The link works only once.
        var reusedPage = await browser.GetPageAsync(resetLink);
        var reused = await Browser.ReadPageAsync(await browser.SubmitAsync(reusedPage, new Dictionary<string, string>
        {
            ["Input.Password"] = "yet another passphrase",
            ["Input.ConfirmPassword"] = "yet another passphrase",
        }));
        reused.Text.ShouldContain("This link is invalid or has expired.");
    }

    [Fact]
    public async Task PasswordReset_LiftsALockout()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await browser.SignInAsync(user.Email, "not the password at all");
        }

        var forgotPage = await browser.GetPageAsync("/forgot-password");
        using var sent = await browser.SubmitAsync(forgotPage, new Dictionary<string, string> { ["Input.Email"] = user.Email });
        var resetPage = await browser.GetPageAsync(CapturingEmailSender.LinkIn(await server.Emails.WaitForAsync(user.Email, "Reset your Kimlik password")));
        using var reset = await browser.SubmitAsync(resetPage, new Dictionary<string, string>
        {
            ["Input.Password"] = NewPassword,
            ["Input.ConfirmPassword"] = NewPassword,
        });

        using var signedIn = await new Browser(server).SignInAsync(user.Email, NewPassword);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task PasswordReset_ForUnknownAddress_LooksTheSame_AndSendsNothing()
    {
        var email = $"unknown-{Guid.NewGuid():N}@example.com";
        using var browser = new Browser(server);

        var page = await browser.GetPageAsync("/forgot-password");
        var sent = await Browser.ReadPageAsync(await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Email"] = email }));

        sent.Text.ShouldContain("If an account exists for that address");

        // Give the outbox several polls to prove nothing was queued for the address.
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        server.Emails.SentTo(email).ShouldBeEmpty();
    }

    [Fact]
    public async Task VerificationLink_CanBeRequestedAgain()
    {
        var user = await server.CreateUserAsync(emailConfirmed: false);
        using var browser = new Browser(server);

        var page = await browser.GetPageAsync("/verify-email/resend");
        await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Email"] = user.Email });

        var link = CapturingEmailSender.LinkIn(await server.Emails.WaitForAsync(user.Email, "Confirm your email address"));
        (await browser.GetPageAsync(link)).Text.ShouldContain("Your email address is confirmed.");
    }

    [Fact]
    public async Task TamperedVerificationLink_IsRejected()
    {
        var user = await server.CreateUserAsync(emailConfirmed: false);
        using var browser = new Browser(server);

        var page = await browser.GetPageAsync($"/verify-email?userId={user.Id}&token=forged");

        page.Text.ShouldContain("This link is invalid or has expired.");
    }

    private static async Task SignUpAsync(Browser browser, string email)
    {
        var page = await browser.GetPageAsync("/signup");
        using var response = await browser.SubmitAsync(page, new Dictionary<string, string>
        {
            ["Input.GivenName"] = "Grace",
            ["Input.FamilyName"] = "Hopper",
            ["Input.Email"] = email,
            ["Input.Password"] = TestUsers.Password,
        });

        response.Headers.Location!.ToString().ShouldBe("/signup/check-email");
    }
}
