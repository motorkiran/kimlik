using System.Net;
using System.Net.Http.Headers;
using Kimlik.AspNetCore;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Sdk;

/// <summary>An API that meters calls with Kimlik.AspNetCore, against the Kimlik server under test.</summary>
public sealed class UsageSdkTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Calls_AreServed_UntilTheMonthsAllowanceRunsOut()
    {
        using var admin = await server.CreateApiClientAsync();
        var catalog = await MeteredCatalog.CreateAsync(admin, limit: 2);
        var user = await server.CreateUserAsync();
        using var subscribed = await admin.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = SubscriberType.User,
            SubscriberId = user.Id,
            Plan = catalog.Plan,
        });
        await using var api = await StartApiAsync(catalog.ApiCalls);
        var (browser, _, tokens) = await server.SignInAndRedeemAsync(user);
        browser.Dispose();
        using var http = api.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());

        var answers = new List<HttpStatusCode>();
        for (var call = 0; call < 3; call++)
        {
            using var response = await http.GetAsync("/call", CancellationToken);
            answers.Add(response.StatusCode);
        }

        answers.ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests]);
    }

    private async Task<WebApplication> StartApiAsync(string feature)
    {
        var meter = await server.CreateServiceClientAsync(KimlikScopes.Api);
        await server.AssignToClientAsync(meter.ClientId, await server.CreateRoleWithAsync(SystemPermissions.PlansRead, SystemPermissions.UsageWrite));

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
            options.ClientId = meter.ClientId;
            options.ClientSecret = meter.ClientSecret;
        });
        builder.Services.AddKimlikEntitlements();

        // Kimlik runs in memory, so every call to it goes through the test server.
        builder.Services.Configure<JwtBearerOptions>(KimlikDefaults.AuthenticationScheme, options => options.BackchannelHttpHandler = server.Server.CreateHandler());
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => server.Server.CreateHandler()));

        var app = builder.Build();
        app.MapGet("/call", async (KimlikUser caller, IKimlikUsage usage) =>
            await usage.TryConsumeAsync(caller, feature) ? Results.Ok("served") : Results.StatusCode(StatusCodes.Status429TooManyRequests))
            .RequireAuthorization();

        await app.StartAsync(CancellationToken);
        return app;
    }
}
