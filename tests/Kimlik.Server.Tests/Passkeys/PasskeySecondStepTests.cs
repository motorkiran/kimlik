using System.Text;
using System.Text.Json;
using Kimlik.Domain.Auditing;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Mfa;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Passkeys;

/// <summary>Passkeys as the second step after a password, instead of an authenticator code.</summary>
public sealed class PasskeySecondStepTests(KimlikServerFixture server)
{
    [Fact]
    public async Task AccountWithAnAuthenticatorApp_CanUseAPasskeyInstead()
    {
        var (user, authenticator) = await UserWithPasskeyAsync();
        using var _ = authenticator;
        await server.EnableMfaAsync(user.Id);
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId);
        using var browser = new Browser(server);

        using var signIn = await browser.SignInAsync(user.Email, user.Password, request.Url);
        var page = await browser.GetPageAsync(signIn.Headers.Location!.OriginalString);
        page.Document.QuerySelector("input[name='Input.Code']").ShouldNotBeNull();
        using var verified = await browser.RunPasskeyFormAsync(page, "form", options => authenticator.Get(options), buttonSelector: "#passkey-verify");
        verified.Headers.Location!.OriginalString.ShouldBe(request.Url);
        using var callback = await browser.FollowAsync(verified);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Methods(tokens).ShouldBe(["pwd", "pop", "mfa"]);
    }

    [Fact]
    public async Task RequiredSecondFactor_IsAPasskey_ForAnAccountWithoutAnApp()
    {
        var (user, authenticator) = await UserWithPasskeyAsync();
        using var _ = authenticator;
        var organization = await server.CreateOrganizationAsync(requireMfa: true, user.Id);
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId) { Organization = organization.Slug };
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        using var stepUp = await browser.GetAsync(request.Url);
        stepUp.Headers.Location!.OriginalString.ShouldStartWith("/signin/two-factor?");
        var page = await browser.GetPageAsync(stepUp.Headers.Location.OriginalString);
        page.Document.QuerySelector("input[name='Input.Code']").ShouldBeNull();
        using var verified = await browser.RunPasskeyFormAsync(page, "form", options => authenticator.Get(options), buttonSelector: "#passkey-verify");
        using var callback = await browser.FollowAsync(verified);

        AuthorizationRequest.ReadCallback(callback).ShouldContainKey("code");
    }

    [Fact]
    public async Task SomeoneElsesPasskey_DoesNotVerifyTheSecondStep()
    {
        var (user, authenticator) = await UserWithPasskeyAsync();
        using var _ = authenticator;
        await server.EnableMfaAsync(user.Id);
        var (_, otherAuthenticator) = await UserWithPasskeyAsync();
        using var __ = otherAuthenticator;
        using var browser = new Browser(server);

        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var page = await browser.GetPageAsync(signIn.Headers.Location!.OriginalString);
        using var refused = await browser.RunPasskeyFormAsync(page, "form", options => otherAuthenticator.Get(options), buttonSelector: "#passkey-verify");

        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("The passkey could not be verified.");
        (await server.QueryDatabaseAsync(context => context.AuditEvents.AnyAsync(auditEvent =>
            auditEvent.SubjectId == user.Id.ToString() && auditEvent.Action == AuditActions.UserMfaChallengeFailed)))
            .ShouldBeTrue();
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
        var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty("access_token").GetString()!.Split('.')[1])));
        return [.. payload.RootElement.GetProperty("amr").EnumerateArray().Select(method => method.GetString()!)];
    }
}
