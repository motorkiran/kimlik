using System.Net;
using System.Text.Json;
using Kimlik.Contracts.Management;

namespace Kimlik.Server.Tests.Api;

public sealed class PlanApiTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Plans_SetAValueForEveryFeature()
    {
        using var api = await server.CreateApiClientAsync();
        var (export, projects, exportId) = await CreateFeaturesAsync(api);

        using var created = await api.Http.SendJsonAsync(HttpMethod.Post, "/api/v1/plans", $$"""
            { "key": "{{NewKey("pro")}}", "name": "Pro", "features": { "{{export}}": true, "{{projects}}": null } }
            """);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var plan = await created.ReadAsync<PlanResponse>();
        plan.Features[export].GetBoolean().ShouldBeTrue();
        plan.Features[projects].ValueKind.ShouldBe(JsonValueKind.Null);

        // Features the update leaves out are off, or zero.
        using var updated = await api.Http.SendJsonAsync(HttpMethod.Patch, $"/api/v1/plans/{plan.Id}", $$"""
            { "isArchived": true, "features": { "{{projects}}": 5 } }
            """);
        var changed = await updated.ReadAsync<PlanResponse>();
        changed.IsArchived.ShouldBeTrue();
        changed.Features[export].GetBoolean().ShouldBeFalse();
        changed.Features[projects].GetInt64().ShouldBe(5);

        using var archived = await api.Http.GetAsync("/api/v1/plans?archived=true", CancellationToken);
        (await archived.ReadAsync<Page<PlanResponse>>()).Items.ShouldContain(item => item.Id == plan.Id);

        using var deletedFeature = await api.Http.DeleteAsync($"/api/v1/features/{exportId}", CancellationToken);
        using var fetched = await api.Http.GetAsync($"/api/v1/plans/{plan.Id}", CancellationToken);
        (await fetched.ReadAsync<PlanResponse>()).Features.Keys.ShouldNotContain(export);
    }

    [Theory]
    [InlineData("""{ "{export}": 3 }""", "plan.invalid_feature_value")]
    [InlineData("""{ "{projects}": -1 }""", "plan.invalid_feature_value")]
    [InlineData("""{ "{projects}": 1.5 }""", "plan.invalid_feature_value")]
    [InlineData("""{ "{projects}": true }""", "plan.invalid_feature_value")]
    [InlineData("""{ "no_such_feature": true }""", "plan.unknown_feature")]
    public async Task InvalidFeatureValues_AreRejected(string values, string code)
    {
        using var api = await server.CreateApiClientAsync();
        var (export, projects, _) = await CreateFeaturesAsync(api);
        var features = values.Replace("{export}", export, StringComparison.Ordinal).Replace("{projects}", projects, StringComparison.Ordinal);

        using var response = await api.Http.SendJsonAsync(HttpMethod.Post, "/api/v1/plans", $$"""{ "key": "{{NewKey("plan")}}", "name": "Plan", "features": {{features}} }""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe(code);
    }

    [Fact]
    public async Task Keys_AreValidated_AndUnique()
    {
        using var api = await server.CreateApiClientAsync();
        var key = NewKey("seats");
        using var first = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = key, Name = "Seats", Type = FeatureType.Limit });

        using var duplicate = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = key, Name = "Seats", Type = FeatureType.Limit });
        using var malformed = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = "Max Seats", Name = "Seats", Type = FeatureType.Limit });

        (await duplicate.ReadProblemCodeAsync()).ShouldBe("feature.already_exists");
        (await malformed.ReadProblemCodeAsync()).ShouldBe("plan.invalid_feature_key");
    }

    [Fact]
    public async Task Provisioning_DefinesFeaturesAndPlans_Idempotently()
    {
        using var api = await server.CreateApiClientAsync();
        var export = NewKey("export");
        var free = NewKey("free");
        var document = $$"""
            {
              "features": [{ "key": "{{export}}", "name": "PDF export", "type": "boolean" }],
              "plans": [{ "key": "{{free}}", "name": "Free", "features": { "{{export}}": false } }]
            }
            """;

        using var applied = await api.Http.SendJsonAsync(HttpMethod.Post, "/api/v1/provisioning", document);
        (await applied.ReadAsync<ProvisioningResult>()).ShouldBe(new ProvisioningResult(Created: 2, Updated: 0, Unchanged: 0));

        using var again = await api.Http.SendJsonAsync(HttpMethod.Post, "/api/v1/provisioning", document);
        (await again.ReadAsync<ProvisioningResult>()).ShouldBe(new ProvisioningResult(Created: 0, Updated: 0, Unchanged: 2));

        using var exported = await api.Http.GetAsync("/api/v1/provisioning", CancellationToken);
        (await exported.ReadAsync<ProvisioningDocument>()).Plans!.Single(plan => plan.Key == free).Features![export].GetBoolean().ShouldBeFalse();
    }

    /// <summary>A boolean feature and a limit, with unique keys; also returns the ID of the boolean one.</summary>
    private static async Task<(string Export, string Projects, Guid ExportId)> CreateFeaturesAsync(ApiClient api)
    {
        var export = NewKey("export");
        var projects = NewKey("projects");
        using var first = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = export, Name = "PDF export", Type = FeatureType.Boolean });
        using var second = await api.Http.PostJsonAsync("/api/v1/features", new CreateFeatureRequest { Key = projects, Name = "Projects", Type = FeatureType.Limit });
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (export, projects, (await first.ReadAsync<FeatureResponse>()).Id);
    }

    private static string NewKey(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..30];
}
