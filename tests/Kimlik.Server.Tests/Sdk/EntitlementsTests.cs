using System.Net;
using System.Net.Http.Headers;
using Kimlik.AspNetCore;
using Kimlik.AspNetCore.Authorization;
using Kimlik.AspNetCore.Entitlements;
using Kimlik.Client;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Plans;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Sdk;

/// <summary>An API that gates a feature and reads a limit with Kimlik.AspNetCore, against the Kimlik server under test.</summary>
public sealed class EntitlementsTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Features_AndLimits_FollowTheCallersPlan()
    {
        using var admin = await server.CreateApiClientAsync();
        var catalog = await TestCatalog.CreateAsync(admin);
        var freeUser = await server.CreateUserAsync();
        var proUser = await server.CreateUserAsync();
        await SubscribeAsync(admin, freeUser.Id, catalog.Free);
        await SubscribeAsync(admin, proUser.Id, catalog.Pro);
        await using var api = await StartApiAsync(catalog);

        using var free = WithToken(api, await AccessTokenAsync(freeUser));
        using var pro = WithToken(api, await AccessTokenAsync(proUser));

        using var freeExport = await free.GetAsync("/export", CancellationToken);
        using var proExport = await pro.GetAsync("/export", CancellationToken);
        freeExport.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        proExport.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await free.GetStringAsync("/limit", CancellationToken)).ShouldBe("3");
        (await pro.GetStringAsync("/limit", CancellationToken)).ShouldBe("unlimited");
    }

    [Fact]
    public async Task SubscriberWithOverrides_GetsItsOwnValues()
    {
        using var admin = await server.CreateApiClientAsync();
        var catalog = await TestCatalog.CreateAsync(admin);
        var user = await server.CreateUserAsync();
        using var created = await admin.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = SubscriberType.User,
            SubscriberId = user.Id,
            Plan = catalog.Free,
            FeatureOverrides = new Dictionary<string, System.Text.Json.JsonElement>
            {
                [catalog.Projects] = System.Text.Json.JsonSerializer.SerializeToElement(25),
                [catalog.Export] = System.Text.Json.JsonSerializer.SerializeToElement(true),
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var api = await StartApiAsync(catalog);

        using var http = WithToken(api, await AccessTokenAsync(user));

        (await http.GetStringAsync("/limit", CancellationToken)).ShouldBe("25");
        using var export = await http.GetAsync("/export", CancellationToken);
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<WebApplication> StartApiAsync(TestCatalog catalog)
    {
        var reader = await server.CreateServiceClientAsync(KimlikScopes.Api);
        await server.AssignToClientAsync(reader.ClientId, await server.CreateRoleWithAsync(SystemPermissions.PlansRead, SystemPermissions.SubscriptionsRead));

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddKimlik(options =>
        {
            options.Authority = new Uri(TestConfiguration.PublicUrl);
            options.Audience = TestClients.ApiResource;
            options.RequireHttpsMetadata = false;
        });
        builder.Services.AddKimlikClient(options =>
        {
            options.Authority = new Uri(TestConfiguration.PublicUrl);
            options.ClientId = reader.ClientId;
            options.ClientSecret = reader.ClientSecret;
        });
        builder.Services.AddKimlikEntitlements();

        // Kimlik runs in memory, so every call to it goes through the test server.
        builder.Services.Configure<JwtBearerOptions>(KimlikDefaults.AuthenticationScheme, options => options.BackchannelHttpHandler = server.Server.CreateHandler());
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => server.Server.CreateHandler()));

        var app = builder.Build();
        app.MapGet("/export", () => "exported").RequireFeature(catalog.Export);
        app.MapGet("/limit", async (KimlikUser caller, IKimlikEntitlements entitlements) =>
            await entitlements.GetLimitAsync(caller, catalog.Projects) is { } limit ? limit.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unlimited")
            .RequireAuthorization();

        await app.StartAsync(CancellationToken);
        return app;
    }

    private static async Task SubscribeAsync(ApiClient admin, Guid userId, string plan)
    {
        using var created = await admin.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest { SubscriberType = SubscriberType.User, SubscriberId = userId, Plan = plan });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private async Task<string> AccessTokenAsync(TestUser user)
    {
        var (browser, _, tokens) = await server.SignInAndRedeemAsync(user);
        browser.Dispose();
        return tokens.GetProperty("access_token").GetString()!;
    }

    private static HttpClient WithToken(WebApplication api, string token)
    {
        var http = api.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
