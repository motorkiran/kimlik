using System.Net;
using Kimlik.Application.Settings;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Infrastructure.Settings;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Settings;

/// <summary>
/// Product settings changed at runtime (ADR 0002). They apply to the whole installation, so each test runs its own Kimlik
/// on a database of its own, and changes settings there as Kimlik itself.
/// </summary>
public sealed class SettingsTests(KimlikServerFixture server)
{
    [Fact]
    public async Task Registration_ClosesAndOpens_WithoutARestart()
    {
        await using var kimlik = await StartAsync();
        using var browser = new Browser(kimlik);
        (await browser.GetPageAsync("/signup")).Document.QuerySelector("form").ShouldNotBeNull();

        (await UpdateAsync(kimlik, ("Kimlik:Accounts:Registration", "InviteOnly"))).IsSuccess.ShouldBeTrue();
        (await browser.GetPageAsync("/signup")).Text.ShouldContain("invitation");
        var registration = Describe(kimlik).Single(setting => setting.Key == "Kimlik:Accounts:Registration");
        registration.Value.ShouldBe("InviteOnly");
        registration.Source.ShouldBe(SettingSource.Database);

        (await UpdateAsync(kimlik, ("Kimlik:Accounts:Registration", null))).IsSuccess.ShouldBeTrue();
        (await browser.GetPageAsync("/signup")).Document.QuerySelector("form").ShouldNotBeNull();
        Describe(kimlik).Single(setting => setting.Key == "Kimlik:Accounts:Registration").Source.ShouldBe(SettingSource.Default);
    }

    [Fact]
    public async Task RequiringMfaForEveryone_AppliesToTheNextSignIn()
    {
        await using var kimlik = await StartAsync();
        var user = await CreateUserAsync(kimlik);

        (await UpdateAsync(kimlik, ("Kimlik:Mfa:RequireForEveryone", "true"))).IsSuccess.ShouldBeTrue();
        using var browser = new Browser(kimlik);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);

        signedIn.Headers.Location!.OriginalString.ShouldStartWith("/signin/set-up-two-factor");
    }

    [Fact]
    public async Task IdentityFollowsTheAccountSettings()
    {
        await using var kimlik = await StartAsync();
        (await UpdateAsync(kimlik, ("Kimlik:Accounts:PasswordMinimumLength", "20"))).IsSuccess.ShouldBeTrue();

        await using var scope = kimlik.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var created = await users.CreateAsync(User.Create($"user-{Guid.NewGuid():N}@example.com", null, null, "en", DateTimeOffset.UtcNow), "only sixteen chr");

        created.Errors.ShouldContain(error => error.Code == "PasswordTooShort");
    }

    [Fact]
    public async Task InvalidValues_AreRefused_AndNothingChanges()
    {
        await using var kimlik = await StartAsync();

        (await UpdateAsync(kimlik, ("Kimlik:Accounts:PasswordMinimumLength", "4"))).Error!.Code.ShouldBe("settings.invalid");
        (await UpdateAsync(kimlik, ("Kimlik:Accounts:Registration", "Sometimes"))).Error!.Code.ShouldBe("settings.invalid_value");
        (await UpdateAsync(kimlik, ("Kimlik:Database:MigrateOnStartup", "false"))).Error!.Code.ShouldBe("settings.unknown");
        (await UpdateAsync(kimlik, ("Kimlik:Captcha:Provider", "Turnstile"))).Error!.Code.ShouldBe("settings.invalid", "a provider needs its keys");

        Describe(kimlik).ShouldAllBe(setting => setting.Source != SettingSource.Database);
    }

    [Fact]
    public async Task SettingsSetInTheEnvironment_CannotChange()
    {
        await using var kimlik = await StartAsync(new Dictionary<string, string?> { ["Kimlik:Branding:ProductName"] = "Fixed" });

        var name = Describe(kimlik).Single(setting => setting.Key == "Kimlik:Branding:ProductName");
        name.Value.ShouldBe("Fixed");
        name.Editable.ShouldBeFalse();
        (await UpdateAsync(kimlik, ("Kimlik:Branding:ProductName", "Changed"))).Error!.Code.ShouldBe("settings.fixed");
    }

    [Fact]
    public async Task Secrets_AreStoredEncrypted_AndNeverShown()
    {
        await using var kimlik = await StartAsync();

        (await UpdateAsync(
            kimlik, ("Kimlik:Captcha:Provider", "Turnstile"), ("Kimlik:Captcha:SiteKey", "site-key"), ("Kimlik:Captcha:SecretKey", "the secret key")))
            .IsSuccess.ShouldBeTrue();

        var secret = Describe(kimlik).Single(setting => setting.Key == "Kimlik:Captcha:SecretKey");
        secret.Value.ShouldBeNull();
        secret.IsSet.ShouldBeTrue();
        await using var scope = kimlik.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<KimlikDbContext>().Settings.SingleAsync(setting => setting.Key == "Kimlik:Captcha:SecretKey", TestContext.Current.CancellationToken);
        stored.IsSecret.ShouldBeTrue();
        stored.Value.ShouldNotContain("the secret key");
        using var browser = new Browser(kimlik);
        (await browser.GetPageAsync("/signup")).Document.QuerySelector("[data-sitekey='site-key']").ShouldNotBeNull();
    }

    [Fact]
    public async Task SocialProviders_AppearAndGo_WithoutARestart()
    {
        await using var kimlik = await StartAsync();
        using var browser = new Browser(kimlik);
        (await browser.GetPageAsync("/signin")).Text.ShouldNotContain("Continue with Google");

        (await UpdateAsync(kimlik, ("Kimlik:SocialLogin:Google:ClientId", "google-client"), ("Kimlik:SocialLogin:Google:ClientSecret", "google-secret")))
            .IsSuccess.ShouldBeTrue();
        (await browser.GetPageAsync("/signin")).Text.ShouldContain("Continue with Google");
        Describe(kimlik).Single(setting => setting.Key == "Kimlik:SocialLogin:Google:ClientSecret").Value.ShouldBeNull();

        (await UpdateAsync(kimlik, ("Kimlik:SocialLogin:Apple:ClientId", "com.example.web"))).Error!.Code.ShouldBe("settings.invalid", "Apple needs its key");
        (await UpdateAsync(kimlik, ("Kimlik:SocialLogin:Google:ClientId", null))).IsSuccess.ShouldBeTrue();
        (await browser.GetPageAsync("/signin")).Text.ShouldNotContain("Continue with Google");
    }

    [Fact]
    public async Task AnotherInstance_PicksUpChanges()
    {
        var database = $"settings_{Guid.NewGuid():N}";
        await using var first = await StartAsync(database: database);
        await using var second = await StartAsync(database: database);

        (await UpdateAsync(first, ("Kimlik:Branding:ProductName", "Acme ID"))).IsSuccess.ShouldBeTrue();
        await second.Services.LoadStoredSettingsAsync(TestContext.Current.CancellationToken);

        using var browser = new Browser(second);
        (await browser.GetPageAsync("/signin")).Text.ShouldContain("Acme ID");
    }

    [Fact]
    public async Task ChangingSettings_TakesEveryInstallationWideSystemPermission()
    {
        using var settingsManager = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.SettingsRead, SystemPermissions.SettingsWrite));

        using var listed = await settingsManager.Http.GetAsync("/api/v1/settings", TestContext.Current.CancellationToken);
        (await listed.ReadAsync<List<SettingResponse>>()).Select(setting => setting.Key).ShouldContain("Kimlik:Accounts:Registration");
        using var refused = await settingsManager.Http.SendJsonAsync(HttpMethod.Patch, "/api/v1/settings", """{ "values": { "Kimlik:Accounts:Registration": "Disabled" } }""");

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");
    }

    /// <summary>A Kimlik of its own, on a database of its own, with <paramref name="environment"/> set as the environment sets settings.</summary>
    private async Task<WebApplicationFactory<Program>> StartAsync(IDictionary<string, string?>? environment = null, string? database = null)
    {
        var settings = new Dictionary<string, string?>(environment ?? new Dictionary<string, string?>())
        {
            ["ConnectionStrings:Kimlik"] = server.ConnectionStringFor(database ?? $"settings_{Guid.NewGuid():N}"),
        };
        var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings)));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        return kimlik;
    }

    private static async Task<Result<IReadOnlyList<SettingResponse>>> UpdateAsync(WebApplicationFactory<Program> kimlik, params (string Key, string? Value)[] values)
    {
        await using var scope = kimlik.Services.CreateAsyncScope();

        // Kimlik itself makes the change, outside of any request, even if one that ran before left its context behind.
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
        return await scope.ServiceProvider.GetRequiredService<UpdateSettingsHandler>().HandleAsync(
            new UpdateSettingsRequest { Values = values.ToDictionary(value => value.Key, value => value.Value) }, TestContext.Current.CancellationToken);
    }

    private static IReadOnlyList<SettingResponse> Describe(WebApplicationFactory<Program> kimlik)
    {
        using var scope = kimlik.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<GetSettingsHandler>().Handle();
    }

    private static async Task<TestUser> CreateUserAsync(WebApplicationFactory<Program> kimlik)
    {
        await using var scope = kimlik.Services.CreateAsyncScope();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var user = User.Create(email, "Ada", "Lovelace", "en", DateTimeOffset.UtcNow);
        user.EmailConfirmed = true;
        user.MarkPasskeyOffered(DateTimeOffset.UtcNow);
        (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAsync(user, TestUsers.Password)).Succeeded.ShouldBeTrue();
        return new TestUser(user.Id, email, TestUsers.Password);
    }
}
