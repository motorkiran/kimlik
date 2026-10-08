using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Oidc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Mfa;

/// <summary>Signing in through the hosted pages with a second factor.</summary>
public sealed class MfaSignInTests(KimlikServerFixture server)
{
    [Fact]
    public async Task UserWithASecondFactor_EntersACode_AndTokensSaySo()
    {
        var user = await server.CreateUserAsync();
        var factor = await server.EnableMfaAsync(user.Id);
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        var request = new AuthorizationRequest(client.ClientId);

        using var signIn = await browser.SignInAsync(user.Email, user.Password, request.Url);
        var challenge = await browser.GetPageAsync(signIn.Headers.Location!.OriginalString);
        challenge.Text.ShouldContain("Enter the code from your authenticator app.");

        var codes = await TotpCodes.NextThreeAsync(factor.Secret);
        using var verified = await browser.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = codes[1] });
        using var callback = await browser.FollowAsync(await browser.GetAsync(verified.Headers.Location!.OriginalString));
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Methods(tokens, "access_token").ShouldBe(["pwd", "otp", "mfa"]);
        Methods(tokens, "id_token").ShouldBe(["pwd", "otp", "mfa"]);

        // Refreshed tokens keep how the user signed in.
        using var refreshed = await OidcFlows.RefreshAsync(browser.Client, client, tokens.GetProperty("refresh_token").GetString()!);
        Methods(await refreshed.ReadJsonAsync(), "access_token").ShouldBe(["pwd", "otp", "mfa"]);
    }

    [Fact]
    public async Task Session_KeepsHowTheUserSignedIn_WhenItsSecurityStampIsChecked()
    {
        await using var strict = await server.WithStrictSessionsAsync();
        var user = await server.CreateUserAsync();
        var factor = await server.EnableMfaAsync(user.Id);
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(strict);

        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var challenge = await browser.GetPageAsync(signIn.Headers.Location!.OriginalString);
        var codes = await TotpCodes.NextThreeAsync(factor.Secret);
        using var verified = await browser.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = codes[1] });

        var request = new AuthorizationRequest(client.ClientId);
        using var callback = await browser.GetAsync(request.Url);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Methods(tokens, "access_token").ShouldBe(["pwd", "otp", "mfa"]);
    }

    [Fact]
    public async Task PasswordOnlySignIn_SaysSoInTokens()
    {
        var user = await server.CreateUserAsync();

        var (browser, _, tokens) = await server.SignInAndRedeemAsync(user);
        browser.Dispose();

        Methods(tokens, "access_token").ShouldBe(["pwd"]);
    }

    [Fact]
    public async Task WrongOrReusedCodes_AreRefused()
    {
        var user = await server.CreateUserAsync();
        var factor = await server.EnableMfaAsync(user.Id);
        var codes = await TotpCodes.NextThreeAsync(factor.Secret);

        using (var first = new Browser(server))
        {
            using var signIn = await first.SignInAsync(user.Email, user.Password);
            var challenge = await first.GetPageAsync(signIn.Headers.Location!.OriginalString);
            using var wrong = await first.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = "000000" });
            (await Browser.ReadPageAsync(wrong)).Text.ShouldContain("That code is not right.");

            using var verified = await first.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = codes[1] });
            verified.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        using var second = new Browser(server);
        using var again = await second.SignInAsync(user.Email, user.Password);
        var secondChallenge = await second.GetPageAsync(again.Headers.Location!.OriginalString);
        using var reused = await second.SubmitAsync(secondChallenge, new Dictionary<string, string> { ["Input.Code"] = codes[1] });
        (await Browser.ReadPageAsync(reused)).Text.ShouldContain("That code is not right.");
    }

    [Fact]
    public async Task RecoveryCode_SignsInOnce()
    {
        var user = await server.CreateUserAsync();
        var factor = await server.EnableMfaAsync(user.Id);

        (await SignInWithRecoveryCodeAsync(user, factor.RecoveryCodes[0].Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant()))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await SignInWithRecoveryCodeAsync(user, factor.RecoveryCodes[0])).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TrustedBrowser_SkipsTheSecondFactor()
    {
        var user = await server.CreateUserAsync();
        var factor = await server.EnableMfaAsync(user.Id);
        using var browser = new Browser(server);

        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var challenge = await browser.GetPageAsync(signIn.Headers.Location!.OriginalString);
        var codes = await TotpCodes.NextThreeAsync(factor.Secret);
        using var verified = await browser.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = codes[1], ["Input.TrustBrowser"] = "true" });
        using var signOut = await browser.SubmitAsync(await browser.GetPageAsync("/signout"));

        using var again = await browser.SignInAsync(user.Email, user.Password);

        again.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task Administrator_SetsUpASecondFactor_BeforeTheSessionStarts()
    {
        await using var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kimlik:Mfa:RequireForAdministrators"] = "true" })));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        var admin = await server.CreateUserAsync();
        await server.AssignToUserAsync(admin.Id, SystemRoles.Admin);
        using var browser = new Browser(kimlik);

        using var signIn = await browser.SignInAsync(admin.Email, admin.Password);
        signIn.Headers.Location!.OriginalString.ShouldStartWith("/signin/set-up-two-factor");

        // No session yet: the account pages send the user back to sign in.
        using var early = await browser.GetAsync("/select-organization?returnUrl=%2Fconnect%2Fauthorize%3Fx%3D1");
        early.Headers.Location!.AbsolutePath.ShouldBe("/signin");

        var setup = await browser.GetPageAsync(signIn.Headers.Location!.OriginalString);
        var secret = setup.Document.GetElementById("secret")!.TextContent.Replace(" ", string.Empty, StringComparison.Ordinal);
        var codes = await TotpCodes.NextThreeAsync(secret);

        var done = await Browser.ReadPageAsync(await browser.SubmitAsync(setup, new Dictionary<string, string> { ["Code"] = codes[1] }));
        done.Document.QuerySelectorAll("#recovery-codes li").Length.ShouldBe(10);

        using var signedIn = await browser.GetAsync("/select-organization?returnUrl=%2Fconnect%2Fauthorize%3Fx%3D1");
        signedIn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> SignInWithRecoveryCodeAsync(TestUser user, string code)
    {
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var challenge = await browser.GetPageAsync($"{signIn.Headers.Location!.OriginalString}&useRecoveryCode=true");
        return await browser.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = code });
    }

    private static string[] Methods(JsonElement tokens, string token)
    {
        var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty(token).GetString()!.Split('.')[1]))).RootElement;
        return [.. payload.GetProperty("amr").EnumerateArray().Select(value => value.GetString()!)];
    }
}
