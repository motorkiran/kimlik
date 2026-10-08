using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Domain.Auditing;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Passkeys;

/// <summary>Signing in with a passkey, which counts as two factors.</summary>
public sealed class PasskeySignInTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Passkey_SignsIn_AsTwoFactors()
    {
        var (user, authenticator) = await UserWithPasskeyAsync();
        using var _ = authenticator;
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId);
        using var browser = new Browser(server);

        using var signedIn = await browser.SignInWithPasskeyAsync(authenticator, request.Url);
        signedIn.Headers.Location!.OriginalString.ShouldBe(request.Url);
        using var callback = await browser.FollowAsync(signedIn);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Methods(tokens).ShouldBe(["pop", "mfa"]);
        var signIn = await server.QueryDatabaseAsync(context => context.AuditEvents
            .Where(auditEvent => auditEvent.SubjectId == user.Id.ToString() && auditEvent.Action == AuditActions.UserSignedIn)
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .Select(auditEvent => auditEvent.Data)
            .FirstAsync(CancellationToken));
        signIn.ShouldNotBeNull().ShouldContain("\"pop\"");
    }

    [Fact]
    public async Task Passkey_MeetsAnOrganizationsSecondFactorRequirement()
    {
        var (user, authenticator) = await UserWithPasskeyAsync();
        using var _ = authenticator;
        var organization = await server.CreateOrganizationAsync(requireMfa: true, user.Id);
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId) { Organization = organization.Slug };
        using var browser = new Browser(server);

        using var signedIn = await browser.SignInWithPasskeyAsync(authenticator, request.Url);
        using var callback = await browser.FollowAsync(signedIn);

        AuthorizationRequest.ReadCallback(callback).ShouldContainKey("code");
    }

    [Fact]
    public async Task Passkey_OfASuspendedUser_DoesNotSignIn()
    {
        var (user, authenticator) = await UserWithPasskeyAsync();
        using var _ = authenticator;
        using var admin = await server.CreateApiClientAsync();
        using var suspended = await admin.Http.PostAsync($"/api/v1/users/{user.Id}/suspend");
        using var browser = new Browser(server);

        using var refused = await browser.SignInWithPasskeyAsync(authenticator);

        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("This account cannot sign in.");
    }

    [Fact]
    public async Task ClonedPasskey_IsRefused()
    {
        var (_, authenticator) = await UserWithPasskeyAsync();
        using var _ = authenticator;
        using (var first = new Browser(server))
        {
            using var signedIn = await first.SignInWithPasskeyAsync(authenticator);
            signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        // A copy of the authenticator does not know that the original has signed since.
        authenticator.Passkeys[0].SignCount = 0;
        using var browser = new Browser(server);
        using var refused = await browser.SignInWithPasskeyAsync(authenticator);

        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("The passkey could not be verified.");
    }

    [Fact]
    public async Task PasswordLockout_DoesNotStopPasskeys()
    {
        var (user, authenticator) = await UserWithPasskeyAsync();
        using var _ = authenticator;
        using var guesser = new Browser(server);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await guesser.SignInAsync(user.Email, "not the password at all");
        }

        using var browser = new Browser(server);
        using var signedIn = await browser.SignInWithPasskeyAsync(authenticator);

        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        signedIn.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task AnswerWithoutItsOptions_IsRefused()
    {
        var (_, authenticator) = await UserWithPasskeyAsync();
        using var _ = authenticator;
        using var browser = new Browser(server);
        var page = await browser.GetPageAsync("/signin");
        var (options, _) = await browser.FetchPasskeyOptionsAsync(page, "form", "#passkey-sign-in");

        using var refused = await browser.SubmitAsync(
            page,
            new Dictionary<string, string> { ["Credential"] = authenticator.Get(options), ["State"] = "made up" },
            action: page.Document.QuerySelector("#passkey-sign-in")!.GetAttribute("formaction"));

        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("The passkey could not be verified.");
    }

    private async Task<(TestUser User, SoftwareAuthenticator Authenticator)> UserWithPasskeyAsync()
    {
        var user = await server.CreateUserAsync();
        var authenticator = new SoftwareAuthenticator();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        await browser.AddPasskeyAsync(authenticator);
        return (user, authenticator);
    }

    private static string[] Methods(JsonElement tokens)
    {
        var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty("id_token").GetString()!.Split('.')[1])));
        return [.. payload.RootElement.GetProperty("amr").EnumerateArray().Select(value => value.GetString()!)];
    }
}
