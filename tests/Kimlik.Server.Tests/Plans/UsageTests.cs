using System.Net;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;

namespace Kimlik.Server.Tests.Plans;

/// <summary>Metered limits: use recorded per subscriber and month, and enforced at the limit.</summary>
public sealed class UsageTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Use_IsCounted_Enforced_AndCountedOncePerIdempotencyKey()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await MeteredCatalog.CreateAsync(api, limit: 5);
        var user = await server.CreateUserAsync();
        await SubscribeAsync(api, user.Id, catalog.Plan);

        var first = await RecordAsync(api, user.Id, catalog.ApiCalls, quantity: 3, enforce: true);
        first.Used.ShouldBe(3);
        first.Remaining.ShouldBe(2);

        using var refused = await api.Http.PostJsonAsync("/api/v1/usage", Request(user.Id, catalog.ApiCalls, quantity: 3, enforce: true));
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.ReadProblemCodeAsync()).ShouldBe("usage.limit_reached");

        var over = await RecordAsync(api, user.Id, catalog.ApiCalls, quantity: 3, enforce: false);
        over.Used.ShouldBe(6);
        over.Remaining.ShouldBe(0);

        (await RecordAsync(api, user.Id, catalog.ApiCalls, quantity: 1, enforce: false, key: "request-1")).Used.ShouldBe(7);
        (await RecordAsync(api, user.Id, catalog.ApiCalls, quantity: 1, enforce: false, key: "request-1")).Used.ShouldBe(7);

        using var listed = await api.Http.GetAsync($"/api/v1/usage/user/{user.Id}", CancellationToken);
        var usage = (await listed.ReadAsync<List<UsageResponse>>()).Single(item => item.Feature == catalog.ApiCalls);
        usage.Used.ShouldBe(7);
        usage.Limit.ShouldBe(5);
        usage.PeriodEnd.ShouldBe(usage.PeriodStart.AddMonths(1));
    }

    [Fact]
    public async Task ConcurrentUse_NeverGoesPastTheLimit()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await MeteredCatalog.CreateAsync(api, limit: 5);
        var user = await server.CreateUserAsync();
        await SubscribeAsync(api, user.Id, catalog.Plan);

        var answers = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            using var response = await api.Http.PostJsonAsync("/api/v1/usage", Request(user.Id, catalog.ApiCalls, quantity: 1, enforce: true));
            return response.StatusCode;
        }));

        answers.Count(status => status == HttpStatusCode.OK).ShouldBe(5);
        answers.Count(status => status == HttpStatusCode.Conflict).ShouldBe(7);
    }

    [Fact]
    public async Task OnlyMeteredLimits_CountUse()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await MeteredCatalog.CreateAsync(api, limit: 5);
        var user = await server.CreateUserAsync();

        using var notMetered = await api.Http.PostJsonAsync("/api/v1/usage", Request(user.Id, catalog.Projects, quantity: 1, enforce: false));
        (await notMetered.ReadProblemCodeAsync()).ShouldBe("usage.not_metered");

        using var meteredSwitch = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest
        {
            Key = $"switch_{Guid.NewGuid():N}"[..20],
            Name = "Switch",
            Type = FeatureType.Boolean,
            Metered = true,
        });
        (await meteredSwitch.ReadProblemCodeAsync()).ShouldBe("feature.metered_limits_only");
    }

    private static RecordUsageRequest Request(Guid userId, string feature, long quantity, bool enforce, string? key = null) => new()
    {
        SubscriberType = SubscriberType.User,
        SubscriberId = userId,
        Feature = feature,
        Quantity = quantity,
        Enforce = enforce,
        IdempotencyKey = key,
    };

    private static async Task<UsageResponse> RecordAsync(ApiClient api, Guid userId, string feature, long quantity, bool enforce, string? key = null)
    {
        using var response = await api.Http.PostJsonAsync("/api/v1/usage", Request(userId, feature, quantity, enforce, key));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        return await response.ReadAsync<UsageResponse>();
    }

    private static async Task SubscribeAsync(ApiClient api, Guid userId, string plan)
    {
        using var created = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest { SubscriberType = SubscriberType.User, SubscriberId = userId, Plan = plan });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}

/// <summary>A metered limit of API calls, a plain limit of projects, and a plan that sets both; with unique keys.</summary>
internal sealed record MeteredCatalog(string ApiCalls, string Projects, string Plan)
{
    public static async Task<MeteredCatalog> CreateAsync(ApiClient api, long limit)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var (apiCalls, projects, plan) = ($"api_calls_{suffix}", $"projects_{suffix}", $"metered_{suffix}");
        using var calls = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = apiCalls, Name = "API calls", Type = FeatureType.Limit, Metered = true });
        (await calls.ReadAsync<FeatureResponse>()).Metered.ShouldBeTrue();
        using var projectsFeature = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = projects, Name = "Projects", Type = FeatureType.Limit });
        using var created = await api.Http.SendJsonAsync(
            HttpMethod.Post, "/api/v1/plans", $$"""{ "key": "{{plan}}", "name": "Metered", "features": { "{{apiCalls}}": {{limit}}, "{{projects}}": 3 } }""");
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return new MeteredCatalog(apiCalls, projects, plan);
    }
}
