using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.EntityFrameworkCore.Models;

namespace Kimlik.Server.Tests.Admin;

/// <summary>Administrators signing in as a user, for support: what they can see and do, and what they cannot.</summary>
public sealed class ImpersonationTests(KimlikServerFixture server)
{
    /// <summary>The page's own form, after the one in the banner that stops the impersonation.</summary>
    private const string PageForm = "section.card form";

    [Fact]
    public async Task Administrator_SeesWhatTheUserSees_ChangesNothing_AndStops()
    {
        var administrator = await server.CreateUserAsync();
        await server.AssignToUserAsync(administrator.Id, SystemRoles.Admin);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(administrator.Email, administrator.Password);

        var confirmation = await browser.GetPageAsync($"/impersonate/{user.Id}");
        confirmation.Text.ShouldContain($"Sign in as {user.Email}?");
        using var started = await browser.SubmitAsync(confirmation);
        started.Headers.Location!.OriginalString.ShouldBe("/account");

        var account = await browser.GetPageAsync("/account");
        account.Text.ShouldContain($"Signed in as {user.Email}.");
        account.Document.QuerySelector("#impersonation").ShouldNotBeNull();

        // Their account stays as it is, and the admin panel stays closed.
        using var changed = await browser.SubmitAsync(account, new Dictionary<string, string> { ["Input.GivenName"] = "Mallory" }, PageForm);
        changed.Headers.Location!.OriginalString.ShouldBe("/impersonation?blocked=true");
        using var panel = await browser.GetAsync("/admin");
        panel.Headers.Location!.OriginalString.ShouldBe("/impersonation?blocked=true");
        (await server.QueryDatabaseAsync(context => context.Users.Where(candidate => candidate.Id == user.Id).Select(candidate => candidate.GivenName).SingleAsync()))
            .ShouldNotBe("Mallory");

        // Apps get short-lived tokens that name the administrator, and no refresh token.
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId);
        using var callback = await browser.GetAsync(request.Url);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);
        tokens.TryGetProperty("refresh_token", out _).ShouldBeFalse();
        var accessToken = Payload(tokens.GetProperty("access_token").GetString()!);
        accessToken.GetProperty("sub").GetString().ShouldBe(user.Id.ToString());
        accessToken.GetProperty("act").GetProperty("sub").GetString().ShouldBe(administrator.Id.ToString());
        (accessToken.GetProperty("exp").GetInt64() - accessToken.GetProperty("iat").GetInt64()).ShouldBeLessThanOrEqualTo(30 * 60);
        Payload(tokens.GetProperty("id_token").GetString()!).GetProperty("act").GetProperty("sub").GetString().ShouldBe(administrator.Id.ToString());

        var status = await browser.GetPageAsync("/impersonation");
        using var stopped = await browser.SubmitAsync(status);
        stopped.Headers.Location!.OriginalString.ShouldBe($"/signin?returnUrl=%2Fadmin%2Fusers%2F{user.Id}");
        using var signedOut = await browser.GetAsync("/account");
        signedOut.Headers.Location!.AbsolutePath.ShouldBe("/signin");

        var audit = await server.QueryDatabaseAsync(context => context.AuditEvents
            .Where(auditEvent => auditEvent.SubjectId == user.Id.ToString()
                && (auditEvent.Action == AuditActions.UserImpersonationStarted || auditEvent.Action == AuditActions.UserImpersonationEnded))
            .Select(auditEvent => new { auditEvent.Action, auditEvent.ActorId })
            .ToListAsync());
        audit.Select(auditEvent => auditEvent.Action).ShouldBe([AuditActions.UserImpersonationStarted, AuditActions.UserImpersonationEnded], ignoreOrder: true);
        audit.ShouldAllBe(auditEvent => auditEvent.ActorId == administrator.Id.ToString());
    }

    [Fact]
    public async Task ImpersonationToken_ReadsTheAccount_ButChangesNothing_AndRecordsNoConsent()
    {
        var administrator = await server.CreateUserAsync();
        await server.AssignToUserAsync(administrator.Id, SystemRoles.Admin);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(administrator.Email, administrator.Password);
        using var started = await browser.SubmitAsync(await browser.GetPageAsync($"/impersonate/{user.Id}"));

        // Access to Kimlik's own API asks for consent, which the administrator gives for this sign-in only.
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId) { Scope = $"openid {KimlikScopes.Api}" };
        var consent = await Browser.ReadPageAsync(await browser.GetAsync(request.Url));
        using var callback = await browser.SubmitAsync(consent, formSelector: PageForm, submitter: ("consent", "accept"));
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);
        using var me = server.WithToken(tokens.GetProperty("access_token").GetString()!);

        using var read = await me.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var changed = await me.SendJsonAsync(HttpMethod.Patch, "/api/v1/me", """{ "givenName": "Mallory" }""");
        changed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await changed.ReadProblemCodeAsync()).ShouldBe("account.impersonating");

        var consents = await server.QueryDatabaseAsync(context => context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>()
            .CountAsync(authorization => authorization.Subject == user.Id.ToString() && authorization.Type == "permanent"));
        consents.ShouldBe(0);
    }

    [Fact]
    public async Task Impersonation_OutlastsTheSessionsSecurityStampCheck()
    {
        await using var strict = await server.WithStrictSessionsAsync();
        var administrator = await server.CreateUserAsync();
        await server.AssignToUserAsync(administrator.Id, SystemRoles.Admin);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(strict);
        using var signIn = await browser.SignInAsync(administrator.Email, administrator.Password);
        using var started = await browser.SubmitAsync(await browser.GetPageAsync($"/impersonate/{user.Id}"));

        // The check rebuilds the session's claims from the user; the administrator must stay on them.
        using var panel = await browser.GetAsync("/admin");
        panel.Headers.Location!.OriginalString.ShouldBe("/impersonation?blocked=true");
        (await browser.GetPageAsync("/account")).Document.QuerySelector("#impersonation").ShouldNotBeNull();
    }

    [Fact]
    public async Task Administrator_CannotSignInAs_SomeoneWithMoreAccess()
    {
        var supportRole = await server.CreateRoleWithAsync(SystemPermissions.UsersRead, SystemPermissions.UsersImpersonate);
        var support = await server.CreateUserAsync();
        await server.AssignToUserAsync(support.Id, supportRole);
        var administrator = await server.CreateUserAsync();
        await server.AssignToUserAsync(administrator.Id, SystemRoles.Admin);
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(support.Email, support.Password);

        var refused = await browser.GetPageAsync($"/impersonate/{administrator.Id}");
        refused.Text.ShouldContain("This user has access to Kimlik that you do not have.");
        refused.Document.QuerySelector("#impersonate").ShouldBeNull();

        // Posting anyway changes nothing.
        var ownAccount = await browser.GetPageAsync("/account");
        using var forced = await browser.SubmitAsync(ownAccount, formSelector: PageForm, action: $"/impersonate/{administrator.Id}");
        forced.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await browser.GetPageAsync("/account")).Text.ShouldContain($"Signed in as {support.Email}.");
    }

    [Fact]
    public async Task Administrator_WithoutThePermission_IsRefused()
    {
        var role = await server.CreateRoleWithAsync(SystemPermissions.UsersRead, SystemPermissions.UsersWrite);
        var administrator = await server.CreateUserAsync();
        await server.AssignToUserAsync(administrator.Id, role);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(administrator.Email, administrator.Password);

        var refused = await browser.GetPageAsync($"/impersonate/{user.Id}");

        refused.Text.ShouldContain("Your roles do not allow this.");
        refused.Document.QuerySelector("#impersonate").ShouldBeNull();
    }

    private static JsonElement Payload(string jwt) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]))).RootElement.Clone();
}
