using System.Net;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Oidc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kimlik.Server.Tests.Api;

public sealed class ApiResourceApiTests(KimlikServerFixture server)
{
    private const string ApiResources = "/api/v1/api-resources";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApiResource_IssuesTokensForItsAudience_UntilDeleted()
    {
        using var api = await server.CreateApiClientAsync();
        var scope = NewScope();

        using var created = await api.Http.PostJsonAsync(ApiResources, new CreateApiResourceRequest
        {
            Scope = scope,
            Audience = "https://shipping.example.com",
            DisplayName = "Manage your shipments",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var resource = await created.ReadAsync<ApiResourceResponse>();
        created.Headers.Location!.OriginalString.ShouldBe($"{ApiResources}/{resource.Id}");
        resource.IsSystem.ShouldBeFalse();

        var serviceClient = await server.CreateServiceClientAsync(scope);
        using var http = server.CreateClient();
        new JsonWebToken(await http.RequestClientCredentialsTokenAsync(serviceClient, scope)).Audiences.ShouldBe(["https://shipping.example.com"]);

        using var described = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{ApiResources}/{resource.Id}", """{ "description": "Shipments and labels" }""");
        var updated = await described.ReadAsync<ApiResourceResponse>();
        updated.Description.ShouldBe("Shipments and labels");
        updated.DisplayName.ShouldBe("Manage your shipments");

        using var deleted = await api.Http.DeleteAsync($"{ApiResources}/{resource.Id}", CancellationToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var refused = await http.PostFormAsync("/connect/token", [new("grant_type", "client_credentials"), new("scope", scope)], serviceClient);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_scope");
    }

    [Fact]
    public async Task Audience_DefaultsToTheScope()
    {
        using var api = await server.CreateApiClientAsync();
        var scope = NewScope();

        using var created = await api.Http.PostJsonAsync(ApiResources, new CreateApiResourceRequest { Scope = scope });

        (await created.ReadAsync<ApiResourceResponse>()).Audience.ShouldBe(scope);
    }

    [Theory]
    [InlineData("Orders", null, "api_resource.invalid_scope")]
    [InlineData("openid", null, "api_resource.reserved_scope")]
    [InlineData("kimlik", null, "api_resource.reserved_scope")]
    [InlineData("orders-{0}", "orders api", "api_resource.invalid_audience")]
    public async Task CreateApiResource_WithInvalidNames_IsRejected(string scope, string? audience, string code)
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.PostJsonAsync(ApiResources, new CreateApiResourceRequest
        {
            Scope = scope.Replace("{0}", Guid.NewGuid().ToString("N")[..8], StringComparison.Ordinal),
            Audience = audience,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe(code);
    }

    [Fact]
    public async Task CreateApiResource_WithTakenScope_IsConflict()
    {
        using var api = await server.CreateApiClientAsync();
        var scope = NewScope();
        using var first = await api.Http.PostJsonAsync(ApiResources, new CreateApiResourceRequest { Scope = scope });

        using var second = await api.Http.PostJsonAsync(ApiResources, new CreateApiResourceRequest { Scope = scope });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadProblemCodeAsync()).ShouldBe("api_resource.already_exists");
    }

    [Fact]
    public async Task KimlikApi_IsListed_ButReadOnly()
    {
        using var api = await server.CreateApiClientAsync();

        using var listed = await api.Http.GetAsync($"{ApiResources}?q={KimlikScopes.Api}", CancellationToken);
        var kimlik = (await listed.ReadAsync<Page<ApiResourceResponse>>()).Items.Single(resource => resource.Scope == KimlikScopes.Api);
        kimlik.IsSystem.ShouldBeTrue();

        using var described = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{ApiResources}/{kimlik.Id}", """{ "displayName": "Ours now" }""");
        using var deleted = await api.Http.DeleteAsync($"{ApiResources}/{kimlik.Id}", CancellationToken);

        foreach (var response in new[] { described, deleted })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await response.ReadProblemCodeAsync()).ShouldBe("api_resource.system_read_only");
        }
    }

    private static string NewScope() => $"shipping-{Guid.NewGuid():N}";
}
