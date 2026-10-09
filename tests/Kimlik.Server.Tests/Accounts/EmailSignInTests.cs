using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kimlik.Domain.Users;
using Kimlik.Server.Tests.Mfa;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Passkeys;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Accounts;

/// <summary>Signing in with a one-time code sent by email, and accounts without a password.</summary>
public sealed partial class EmailSignInTests(KimlikServerFixture server)
{
    [Fact]
    public async Task Code_SignsIn_AsAFirstFactor()
    {
        var user = await server.CreateUserAsync();
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId);
        using var browser = new Browser(server);

        using var asked = await AskForCodeAsync(browser, user.Email, request.Url);
        asked.Headers.Location!.OriginalString.ShouldStartWith("/signin/code");
        var page = await browser.GetPageAsync(asked.Headers.Location.OriginalString);
        page.Text.ShouldContain($"If an account has the address {user.Email}, we sent it a code.");

        using var signedIn = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = await CodeSentToAsync(user.Email) });
        signedIn.Headers.Location!.OriginalString.ShouldBe(request.Url);
        using var callback = await browser.FollowAsync(signedIn);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Methods(tokens).ShouldBe(["email"]);
    }

    [Fact]
    public async Task UnknownAddress_LooksTheSame_AndGetsNothing()
    {
        var email = $"nobody-{Guid.NewGuid():N}@example.com";
        using var browser = new Browser(server);

        using var asked = await AskForCodeAsync(browser, email);
        var page = await browser.GetPageAsync(asked.Headers.Location!.OriginalString);
        using var wrong = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = "123456" });

        page.Text.ShouldContain($"If an account has the address {email}, we sent it a code.");
        (await Browser.ReadPageAsync(wrong)).Text.ShouldContain("That code is not right, or it has expired.");
        server.Emails.SentTo(email).ShouldBeEmpty();
    }

    [Fact]
    public async Task WrongCodes_CountTowardTheLockout_AndACodeWorksOnce()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var asked = await AskForCodeAsync(browser, user.Email);
        var page = await browser.GetPageAsync(asked.Headers.Location!.OriginalString);
        var code = await CodeSentToAsync(user.Email);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = code == "000000" ? "111111" : "000000" });
        }

        // Locked: even the right code gets the same answer.
        using var locked = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = code });
        (await Browser.ReadPageAsync(locked)).Text.ShouldContain("That code is not right, or it has expired.");

        await server.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<User>>();
            await users.SetLockoutEndDateAsync((await users.FindByIdAsync(user.Id.ToString()))!, null);
        });
        using var signedIn = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = code });
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        using var again = new Browser(server);
        using var askedAgain = await AskForCodeAsync(again, user.Email);
        var pageAgain = await again.GetPageAsync(askedAgain.Headers.Location!.OriginalString);
        using var reused = await again.SubmitAsync(pageAgain, new Dictionary<string, string> { ["Input.Code"] = code });
        (await Browser.ReadPageAsync(reused)).Text.ShouldContain("That code is not right, or it has expired.");
    }

    [Fact]
    public async Task Code_Expires_AfterTenMinutes()
    {
        var clock = new ShiftedTimeProvider();
        await using var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(kimlik);
        using var asked = await AskForCodeAsync(browser, user.Email);
        var page = await browser.GetPageAsync(asked.Headers.Location!.OriginalString);
        var code = await CodeSentToAsync(user.Email);

        clock.Offset = TimeSpan.FromMinutes(11);
        using var expired = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = code });

        (await Browser.ReadPageAsync(expired)).Text.ShouldContain("That code is not right, or it has expired.");
    }

    [Fact]
    public async Task SecondFactor_StillFollowsTheCode()
    {
        var user = await server.CreateUserAsync();
        var factor = await server.EnableMfaAsync(user.Id);
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId);
        using var browser = new Browser(server);
        using var asked = await AskForCodeAsync(browser, user.Email, request.Url);
        var page = await browser.GetPageAsync(asked.Headers.Location!.OriginalString);

        using var codeAccepted = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = await CodeSentToAsync(user.Email) });
        codeAccepted.Headers.Location!.OriginalString.ShouldStartWith("/signin/two-factor");
        var challenge = await browser.GetPageAsync(codeAccepted.Headers.Location.OriginalString);
        var codes = await TotpCodes.NextThreeAsync(factor.Secret);
        using var verified = await browser.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = codes[1] });
        using var callback = await browser.FollowAsync(verified);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Methods(tokens).ShouldBe(["email", "otp", "mfa"]);
    }

    [Fact]
    public async Task SignUp_WithoutAPassword_SignsInWithTheCode()
    {
        var email = $"new-{Guid.NewGuid():N}@example.com";
        using var browser = new Browser(server);
        var signUp = await browser.GetPageAsync("/signup");

        using var signedUp = await browser.SubmitAsync(signUp, new Dictionary<string, string> { ["Input.Email"] = email, ["Input.Password"] = string.Empty });
        signedUp.Headers.Location!.OriginalString.ShouldStartWith("/signin/code");
        var page = await browser.GetPageAsync(signedUp.Headers.Location.OriginalString);
        using var signedIn = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = await CodeSentToAsync(email) });

        signedIn.Headers.Location!.OriginalString.ShouldStartWith("/signin/passkey");
        var user = await server.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<User>>();
            var created = (await users.FindByEmailAsync(email))!;
            return (created.EmailConfirmed, HasPassword: await users.HasPasswordAsync(created));
        });
        user.EmailConfirmed.ShouldBeTrue();
        user.HasPassword.ShouldBeFalse();
        server.Emails.SentTo(email).ShouldNotContain(message => message.Subject.Contains("Confirm your email", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SignUp_WithoutAPassword_ForATakenAddress_SendsItsOwnerACode()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        var signUp = await browser.GetPageAsync("/signup");

        using var signedUp = await browser.SubmitAsync(signUp, new Dictionary<string, string> { ["Input.Email"] = user.Email, ["Input.Password"] = string.Empty });

        signedUp.Headers.Location!.OriginalString.ShouldStartWith("/signin/code");
        (await CodeSentToAsync(user.Email)).Length.ShouldBe(6);
    }

    [Fact]
    public async Task RemovedPassword_LeavesTheCode()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var page = await browser.GetPageAsync("/account/password");

        using var wrong = await browser.SubmitAsync(page, new Dictionary<string, string> { ["RemovePassword"] = "not the password" }, "#remove-password form");
        (await Browser.ReadPageAsync(wrong)).Text.ShouldContain("That password is not right.");
        using var removed = await browser.SubmitAsync(page, new Dictionary<string, string> { ["RemovePassword"] = user.Password }, "#remove-password form");
        (await Browser.ReadPageAsync(removed)).Text.ShouldContain("Your password has been removed.");

        using var withPassword = new Browser(server);
        (await Browser.ReadPageAsync(await withPassword.SignInAsync(user.Email, user.Password))).Text.ShouldContain("Invalid email or password.");
        using var withCode = new Browser(server);
        using var asked = await AskForCodeAsync(withCode, user.Email);
        var codePage = await withCode.GetPageAsync(asked.Headers.Location!.OriginalString);
        using var signedIn = await withCode.SubmitAsync(codePage, new Dictionary<string, string> { ["Input.Code"] = await CodeSentToAsync(user.Email) });
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task TurnedOff_NeitherOffersCodes_NorTakesAccountsWithoutAPassword()
    {
        await using var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kimlik:Accounts:EmailSignIn"] = "false" })));
        var user = await server.CreateUserAsync();
        using var browser = new Browser(kimlik);

        (await browser.GetPageAsync("/signin")).Document.QuerySelector("#email-code").ShouldBeNull();
        var signUp = await browser.GetPageAsync("/signup");
        using var refused = await browser.SubmitAsync(
            signUp, new Dictionary<string, string> { ["Input.Email"] = $"new-{Guid.NewGuid():N}@example.com", ["Input.Password"] = string.Empty });
        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("Choose a password.");

        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        (await browser.GetPageAsync("/account/password")).Document.QuerySelector("#remove-password").ShouldBeNull();
    }

    /// <summary>Asks for a code on the sign-in page, as the "Email me a sign-in code" button does.</summary>
    private static async Task<HttpResponseMessage> AskForCodeAsync(Browser browser, string email, string? returnUrl = null)
    {
        var page = await browser.GetPageAsync(returnUrl is null ? "/signin" : $"/signin?ReturnUrl={Uri.EscapeDataString(returnUrl)}");
        return await browser.SubmitAsync(
            page,
            new Dictionary<string, string> { ["Input.Email"] = email },
            action: page.Document.QuerySelector("#email-code")!.GetAttribute("formaction"));
    }

    private async Task<string> CodeSentToAsync(string email) =>
        Code().Match((await server.Emails.WaitForAsync(email, "sign-in code")).Subject).Value;

    private static string[] Methods(JsonElement tokens)
    {
        var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty("id_token").GetString()!.Split('.')[1])));
        return [.. payload.RootElement.GetProperty("amr").EnumerateArray().Select(value => value.GetString()!)];
    }

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex Code();
}
