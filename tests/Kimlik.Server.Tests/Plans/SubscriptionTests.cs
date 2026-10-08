using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Application.Plans;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Organizations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Plans;

public sealed class SubscriptionTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Subscription_GivesItsPlan_UntilThePeriodOfACancellationEnds()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await TestCatalog.CreateAsync(api);
        var user = await server.CreateUserAsync();

        using var created = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = SubscriberType.User,
            SubscriberId = user.Id,
            Plan = catalog.Pro,
            CurrentPeriodEnd = DateTimeOffset.UtcNow.AddDays(30),
            ExternalReference = "sub_123",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var subscription = await created.ReadAsync<SubscriptionResponse>();
        subscription.Status.ShouldBe(SubscriptionStatus.Active);

        var entitlements = await EntitlementsAsync(api, user.Id);
        entitlements.Plan.ShouldBe(catalog.Pro);
        entitlements.Features[catalog.Export].GetBoolean().ShouldBeTrue();
        entitlements.Features[catalog.Projects].ValueKind.ShouldBe(JsonValueKind.Null);

        using var second = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest { SubscriberType = SubscriberType.User, SubscriberId = user.Id, Plan = catalog.Free });
        (await second.ReadProblemCodeAsync()).ShouldBe("subscription.exists");

        using var downgraded = await api.Http.SendJsonAsync(HttpMethod.Patch, $"/api/v1/subscriptions/{subscription.Id}", $$"""{ "plan": "{{catalog.Free}}" }""");
        (await downgraded.ReadAsync<SubscriptionResponse>()).Plan.ShouldBe(catalog.Free);

        using var canceled = await api.Http.PostAsync($"/api/v1/subscriptions/{subscription.Id}/cancel");
        (await canceled.ReadAsync<SubscriptionResponse>()).Status.ShouldBe(SubscriptionStatus.Canceled);
        (await EntitlementsAsync(api, user.Id)).Plan.ShouldBe(catalog.Free);
    }

    [Fact]
    public async Task EndedTrial_GivesNothing_AndIsMarkedExpired()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await TestCatalog.CreateAsync(api);
        var user = await server.CreateUserAsync();
        using var created = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = SubscriberType.User,
            SubscriberId = user.Id,
            Plan = catalog.Pro,
            TrialEndsAt = DateTimeOffset.UtcNow.AddSeconds(1),
        });
        var subscription = await created.ReadAsync<SubscriptionResponse>();
        subscription.Status.ShouldBe(SubscriptionStatus.Trialing);

        await Task.Delay(TimeSpan.FromSeconds(1.2), CancellationToken);

        var entitlements = await EntitlementsAsync(api, user.Id);
        entitlements.Plan.ShouldBeNull();
        entitlements.Features[catalog.Export].GetBoolean().ShouldBeFalse();

        await server.WithServicesAsync(services => services.GetRequiredService<ExpireSubscriptionsHandler>().HandleAsync(CancellationToken));
        using var fetched = await api.Http.GetAsync($"/api/v1/subscriptions/{subscription.Id}", CancellationToken);
        (await fetched.ReadAsync<SubscriptionResponse>()).Status.ShouldBe(SubscriptionStatus.Expired);
    }

    [Fact]
    public async Task Tokens_CarryThePlanOfTheirContext()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await TestCatalog.CreateAsync(api);
        var user = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(user.Id);
        await SubscribeAsync(api, SubscriberType.User, user.Id, catalog.Free);
        await SubscribeAsync(api, SubscriberType.Organization, organization.Id, catalog.Pro);
        var client = await server.CreateWebClientAsync();

        (await PlanClaimAsync(user, client, organization: null)).ShouldBe(catalog.Free);
        (await PlanClaimAsync(user, client, organization.Slug)).ShouldBe(catalog.Pro);
    }

    [Fact]
    public async Task UsersWithoutASubscription_GetTheDefaultPlan()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await TestCatalog.CreateAsync(api);
        var user = await server.CreateUserAsync();

        await using var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kimlik:Plans:DefaultUserPlan"] = catalog.Free })));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);

        await using var scope = kimlik.Services.CreateAsyncScope();
        var entitlements = await scope.ServiceProvider.GetRequiredService<Entitlements>()
            .DescribeAsync(Domain.Plans.Subscriber.User(user.Id), CancellationToken);
        entitlements.Plan.ShouldBe(catalog.Free);
    }

    [Fact]
    public async Task ArchivedPlans_TakeNoNewSubscribers_AndPlansInUseStay()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await TestCatalog.CreateAsync(api);
        var user = await server.CreateUserAsync();
        await SubscribeAsync(api, SubscriberType.User, user.Id, catalog.Pro);

        using var deleted = await api.Http.DeleteAsync($"/api/v1/plans/{catalog.ProId}", CancellationToken);
        (await deleted.ReadProblemCodeAsync()).ShouldBe("plan.in_use");

        using var archived = await api.Http.SendJsonAsync(HttpMethod.Patch, $"/api/v1/plans/{catalog.ProId}", """{ "isArchived": true }""");
        using var refused = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = SubscriberType.User,
            SubscriberId = (await server.CreateUserAsync()).Id,
            Plan = catalog.Pro,
        });
        (await refused.ReadProblemCodeAsync()).ShouldBe("plan.archived");
    }

    [Theory]
    [InlineData("organization", "subscription.subscriber_not_found")]
    [InlineData("past-period", "subscription.invalid")]
    public async Task InvalidSubscriptions_AreRejected(string problem, string code)
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await TestCatalog.CreateAsync(api);
        var user = await server.CreateUserAsync();

        using var response = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = problem == "organization" ? SubscriberType.Organization : SubscriberType.User,
            SubscriberId = user.Id,
            Plan = catalog.Pro,
            CurrentPeriodEnd = problem == "past-period" ? DateTimeOffset.UtcNow.AddDays(-1) : null,
        });

        (await response.ReadProblemCodeAsync()).ShouldBe(code);
    }

    private static async Task SubscribeAsync(ApiClient api, SubscriberType type, Guid id, string plan)
    {
        using var created = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest { SubscriberType = type, SubscriberId = id, Plan = plan });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<EntitlementsResponse> EntitlementsAsync(ApiClient api, Guid userId)
    {
        using var response = await api.Http.GetAsync($"/api/v1/entitlements/user/{userId}", CancellationToken);
        return await response.ReadAsync<EntitlementsResponse>();
    }

    private async Task<string?> PlanClaimAsync(TestUser user, TestWebClient client, string? organization)
    {
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client.ClientId) { Organization = organization };
        using var callback = await browser.GetAsync(request.Url);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty("access_token").GetString()!.Split('.')[1]))).RootElement;
        return payload.GetProperty(KimlikClaimTypes.Plan).GetString();
    }
}

/// <summary>A boolean feature, a limit, and Free and Pro plans, with unique keys.</summary>
internal sealed record TestCatalog(string Export, string Projects, string Free, string Pro, Guid ProId)
{
    public static async Task<TestCatalog> CreateAsync(ApiClient api)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var (export, projects, free, pro) = ($"export_{suffix}", $"projects_{suffix}", $"free_{suffix}", $"pro_{suffix}");

        using var exportFeature = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = export, Name = "PDF export", Type = FeatureType.Boolean });
        using var projectsFeature = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = projects, Name = "Projects", Type = FeatureType.Limit });
        using var freePlan = await api.Http.SendJsonAsync(HttpMethod.Post, "/api/v1/plans", $$"""{ "key": "{{free}}", "name": "Free", "features": { "{{projects}}": 3 } }""");
        using var proPlan = await api.Http.SendJsonAsync(HttpMethod.Post, "/api/v1/plans", $$"""{ "key": "{{pro}}", "name": "Pro", "features": { "{{export}}": true, "{{projects}}": null } }""");
        freePlan.StatusCode.ShouldBe(HttpStatusCode.Created);

        return new TestCatalog(export, projects, free, pro, (await proPlan.ReadAsync<PlanResponse>()).Id);
    }
}
