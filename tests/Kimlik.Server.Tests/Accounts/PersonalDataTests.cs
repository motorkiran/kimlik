using System.Net;
using System.Text.Json;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Organizations;
using Kimlik.Server.Tests.Passkeys;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Accounts;

/// <summary>Exports of what Kimlik holds about a user, for the user and for administrators.</summary>
public sealed class PersonalDataTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_DownloadsTheirData_WithoutThePrivateMetadata()
    {
        var user = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(user.Id);
        using var admin = await server.CreateApiClientAsync();
        using var metadata = await admin.Http.SendJsonAsync(
            HttpMethod.Patch, $"/api/v1/users/{user.Id}", """{ "publicMetadata": { "plan": "pro" }, "privateMetadata": { "crmId": "acc_1" } }""");
        using var authenticator = new SoftwareAuthenticator();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        await browser.AddPasskeyAsync(authenticator, "Laptop");

        var page = await browser.GetPageAsync("/account/data");
        using var download = await browser.SubmitAsync(page, formSelector: "#download-data");

        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        download.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull().ShouldStartWith("kimlik-data-");
        var json = await download.Content.ReadAsStringAsync(CancellationToken);
        var export = JsonSerializer.Deserialize<PersonalDataExport>(json, ApiClients.Json)!;
        export.Profile.Email.ShouldBe(user.Email);
        export.Profile.PublicMetadata["plan"]!.GetValue<string>().ShouldBe("pro");
        export.PrivateMetadata.ShouldBeNull();
        json.ShouldNotContain("acc_1");
        export.Organizations.ShouldHaveSingleItem().Id.ShouldBe(organization.Id);
        export.Passkeys.ShouldHaveSingleItem().Name.ShouldBe("Laptop");
        export.Activity.ShouldContain(auditEvent => auditEvent.Action == AuditActions.UserSignedIn);
        (await AuditedExportsOfAsync(user.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task OldSignIn_MustSignInAgain_ToDownload()
    {
        var clock = new ShiftedTimeProvider();
        await using var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(kimlik);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        clock.Offset = TimeSpan.FromMinutes(11);
        var page = await browser.GetPageAsync("/account/data");

        page.Document.QuerySelector("#download-data").ShouldBeNull();
        page.Text.ShouldContain("For your security, sign in again before you download your data.");
    }

    [Fact]
    public async Task Administrator_ExportsTheData_WithThePrivateMetadata()
    {
        var user = await server.CreateUserAsync();
        using var admin = await server.CreateApiClientAsync();
        using var metadata = await admin.Http.SendJsonAsync(HttpMethod.Patch, $"/api/v1/users/{user.Id}", """{ "privateMetadata": { "crmId": "acc_1" } }""");

        using var exported = await admin.Http.GetAsync($"/api/v1/users/{user.Id}/export", CancellationToken);
        var export = await exported.ReadAsync<PersonalDataExport>();

        export.PrivateMetadata!["crmId"]!.GetValue<string>().ShouldBe("acc_1");
        export.Activity.ShouldContain(auditEvent => auditEvent.Action == AuditActions.UserUpdated);
        (await AuditedExportsOfAsync(user.Id)).ShouldBe(1);
        using var stranger = await server.CreateApiClientAsync(role: null);
        using var forbidden = await stranger.Http.GetAsync($"/api/v1/users/{user.Id}/export", CancellationToken);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminPanel_DownloadsTheExport()
    {
        var administrator = await server.CreateUserAsync();
        await server.AssignToUserAsync(administrator.Id, SystemRoles.Admin);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(administrator.Email, administrator.Password);

        using var download = await browser.GetAsync($"/admin/users/{user.Id}/export");

        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentDisposition!.FileName.ShouldBe($"kimlik-user-{user.Id}.json");
        var actor = await server.QueryDatabaseAsync(context => context.AuditEvents
            .Where(auditEvent => auditEvent.Action == AuditActions.UserDataExported && auditEvent.SubjectId == user.Id.ToString())
            .Select(auditEvent => auditEvent.ActorId)
            .SingleAsync(CancellationToken));
        actor.ShouldBe(administrator.Id.ToString());
    }

    private Task<int> AuditedExportsOfAsync(Guid userId) =>
        server.QueryDatabaseAsync(context => context.AuditEvents.CountAsync(
            auditEvent => auditEvent.Action == AuditActions.UserDataExported && auditEvent.SubjectId == userId.ToString(), CancellationToken));
}
