using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Plans;

/// <summary>Add-ons with quantities and feature overrides on top of a subscriber's plan.</summary>
public sealed class AddOnTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Entitlements_ArePlan_ThenAddOns_ThenOverrides()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await AddOnCatalog.CreateAsync(api);
        var user = await server.CreateUserAsync();

        using var created = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = SubscriberType.User,
            SubscriberId = user.Id,
            Plan = catalog.Team,
            AddOns = new Dictionary<string, int> { [catalog.ExtraSeats] = 2, [catalog.Unlimited] = 1 },
            FeatureOverrides = new Dictionary<string, JsonElement> { [catalog.Export] = JsonSerializer.SerializeToElement(false) },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(CancellationToken));
        var subscription = await created.ReadAsync<SubscriptionResponse>();
        subscription.AddOns.ShouldBe(new Dictionary<string, int> { [catalog.ExtraSeats] = 2, [catalog.Unlimited] = 1 }, ignoreOrder: true);

        var entitlements = await EntitlementsAsync(api, user.Id);
        entitlements[catalog.Seats].GetInt64().ShouldBe(5 + (2 * 10));
        entitlements[catalog.Projects].ValueKind.ShouldBe(JsonValueKind.Null);
        entitlements[catalog.Export].GetBoolean().ShouldBeFalse();

        using var updated = await api.Http.SendJsonAsync(
            HttpMethod.Patch, $"/api/v1/subscriptions/{subscription.Id}", $$"""{ "addOns": {}, "featureOverrides": { "{{catalog.Seats}}": 50 } }""");
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        entitlements = await EntitlementsAsync(api, user.Id);
        entitlements[catalog.Seats].GetInt64().ShouldBe(50);
        entitlements[catalog.Projects].GetInt64().ShouldBe(3);
        entitlements[catalog.Export].GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Tokens_SayWhenEntitlementsAreTheSubscribersOwn()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await AddOnCatalog.CreateAsync(api);
        var plain = await server.CreateUserAsync();
        var custom = await server.CreateUserAsync();
        await SubscribeAsync(api, plain.Id, catalog.Team, new Dictionary<string, int>());
        await SubscribeAsync(api, custom.Id, catalog.Team, new Dictionary<string, int> { [catalog.ExtraSeats] = 1 });

        (await TokenAsync(plain)).TryGetProperty(KimlikClaimTypes.CustomEntitlements, out _).ShouldBeFalse();
        var token = await TokenAsync(custom);
        token.GetProperty(KimlikClaimTypes.Plan).GetString().ShouldBe(catalog.Team);
        token.GetProperty(KimlikClaimTypes.CustomEntitlements).GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task PlansAndAddOns_KeepToTheirKinds()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await AddOnCatalog.CreateAsync(api);
        var user = await server.CreateUserAsync();

        using var toAnAddOn = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = SubscriberType.User,
            SubscriberId = user.Id,
            Plan = catalog.ExtraSeats,
        });
        (await toAnAddOn.ReadProblemCodeAsync()).ShouldBe("plan.not_a_base_plan");

        using var planAsAddOn = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = SubscriberType.User,
            SubscriberId = user.Id,
            Plan = catalog.Team,
            AddOns = new Dictionary<string, int> { [catalog.Team] = 1 },
        });
        (await planAsAddOn.ReadProblemCodeAsync()).ShouldBe("plan.not_an_add_on");

        using var none = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = SubscriberType.User,
            SubscriberId = user.Id,
            Plan = catalog.Team,
            AddOns = new Dictionary<string, int> { [catalog.ExtraSeats] = 0 },
        });
        (await none.ReadProblemCodeAsync()).ShouldBe("subscription.invalid_quantity");

        await SubscribeAsync(api, user.Id, catalog.Team, new Dictionary<string, int> { [catalog.ExtraSeats] = 1 });
        using var deleted = await api.Http.DeleteAsync($"/api/v1/plans/{catalog.ExtraSeatsId}", CancellationToken);
        (await deleted.ReadProblemCodeAsync()).ShouldBe("plan.in_use");
    }

    private static async Task SubscribeAsync(ApiClient api, Guid userId, string plan, Dictionary<string, int> addOns)
    {
        using var created = await api.Http.PostJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            SubscriberType = SubscriberType.User,
            SubscriberId = userId,
            Plan = plan,
            AddOns = addOns,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<IReadOnlyDictionary<string, JsonElement>> EntitlementsAsync(ApiClient api, Guid userId)
    {
        using var response = await api.Http.GetAsync($"/api/v1/entitlements/user/{userId}", CancellationToken);
        return (await response.ReadAsync<EntitlementsResponse>()).Features;
    }

    private async Task<JsonElement> TokenAsync(TestUser user)
    {
        var (browser, _, tokens) = await server.SignInAndRedeemAsync(user);
        browser.Dispose();
        return JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty("access_token").GetString()!.Split('.')[1]))).RootElement.Clone();
    }
}

/// <summary>
/// Three features, a Team plan (5 seats, 3 projects, no export), an add-on of 10 seats a unit, and one that makes
/// projects unlimited and turns export on; all with unique keys.
/// </summary>
internal sealed record AddOnCatalog(string Seats, string Projects, string Export, string Team, string ExtraSeats, Guid ExtraSeatsId, string Unlimited)
{
    public static async Task<AddOnCatalog> CreateAsync(ApiClient api)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var (seats, projects, export) = ($"seats_{suffix}", $"projects_{suffix}", $"export_{suffix}");
        var (team, extraSeats, unlimited) = ($"team_{suffix}", $"extra_seats_{suffix}", $"unlimited_{suffix}");

        using var seatsFeature = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = seats, Name = "Seats", Type = FeatureType.Limit });
        using var projectsFeature = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = projects, Name = "Projects", Type = FeatureType.Limit });
        using var exportFeature = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = export, Name = "Export", Type = FeatureType.Boolean });
        using var teamPlan = await api.Http.SendJsonAsync(
            HttpMethod.Post, "/api/v1/plans", $$"""{ "key": "{{team}}", "name": "Team", "features": { "{{seats}}": 5, "{{projects}}": 3 } }""");
        using var seatsAddOn = await api.Http.SendJsonAsync(
            HttpMethod.Post, "/api/v1/plans", $$"""{ "key": "{{extraSeats}}", "name": "Extra seats", "kind": "addOn", "features": { "{{seats}}": 10 } }""");
        using var unlimitedAddOn = await api.Http.SendJsonAsync(
            HttpMethod.Post, "/api/v1/plans", $$"""{ "key": "{{unlimited}}", "name": "Unlimited", "kind": "addOn", "features": { "{{projects}}": null, "{{export}}": true } }""");
        teamPlan.StatusCode.ShouldBe(HttpStatusCode.Created);
        unlimitedAddOn.StatusCode.ShouldBe(HttpStatusCode.Created);
        var addOn = await seatsAddOn.ReadAsync<PlanResponse>();
        addOn.Kind.ShouldBe(PlanKind.AddOn);

        return new AddOnCatalog(seats, projects, export, team, extraSeats, addOn.Id, unlimited);
    }
}
