using System.Net;
using Kimlik.Contracts;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Tests.Oidc;

public sealed class CorsTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BrowserApp_OfANewClient_MayCallTheTokenEndpoint()
    {
        var origin = await CreateSpaAsync();
        using var http = server.CreateClient();

        using var preflight = await SendAsync(http, HttpMethod.Options, "/connect/token", origin, ("Access-Control-Request-Method", "POST"));
        using var discovery = await SendAsync(http, HttpMethod.Get, "/.well-known/openid-configuration", origin);

        preflight.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        AllowedOrigin(preflight).ShouldBe(origin);
        preflight.Headers.GetValues("Access-Control-Allow-Methods").ShouldContain(methods => methods.Contains("POST", StringComparison.Ordinal));
        AllowedOrigin(discovery).ShouldBe(origin);
    }

    [Fact]
    public async Task UnknownOrigin_GetsNoCorsHeaders()
    {
        using var http = server.CreateClient();

        using var response = await SendAsync(http, HttpMethod.Get, "/.well-known/openid-configuration", $"https://{Guid.NewGuid():N}.example.net");

        AllowedOrigin(response).ShouldBeNull();
    }

    [Fact]
    public async Task ManagementApi_IsNotOpenToBrowsers()
    {
        var origin = await CreateSpaAsync();
        using var http = server.CreateClient();

        using var response = await SendAsync(http, HttpMethod.Options, "/api/v1/users", origin, ("Access-Control-Request-Method", "GET"));

        AllowedOrigin(response).ShouldBeNull();
    }

    [Fact]
    public async Task OrganizationEndpoints_AreOpen_ToAppsAllowedTheirScope()
    {
        var allowed = await CreateSpaAsync(KimlikScopes.Organizations);
        var other = await CreateSpaAsync();
        using var http = server.CreateClient();

        using var preflight = await SendAsync(
            http, HttpMethod.Options, $"/api/v1/me/organizations/{Guid.NewGuid()}/members/{Guid.NewGuid()}", allowed,
            ("Access-Control-Request-Method", "DELETE"), ("Access-Control-Request-Headers", "authorization"));
        using var otherApp = await SendAsync(http, HttpMethod.Options, "/api/v1/me/organizations", other, ("Access-Control-Request-Method", "GET"));
        using var account = await SendAsync(http, HttpMethod.Options, "/api/v1/me", allowed, ("Access-Control-Request-Method", "GET"));
        using var apiKeys = await SendAsync(
            http, HttpMethod.Options, $"/api/v1/me/organizations/{Guid.NewGuid()}/api-keys", allowed, ("Access-Control-Request-Method", "GET"));

        AllowedOrigin(preflight).ShouldBe(allowed);
        preflight.Headers.GetValues("Access-Control-Allow-Methods").ShouldContain(methods => methods.Contains("DELETE", StringComparison.Ordinal));
        preflight.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse("calls carry bearer tokens, not cookies");
        AllowedOrigin(otherApp).ShouldBeNull();
        AllowedOrigin(account).ShouldBeNull();
        AllowedOrigin(apiKeys).ShouldBeNull("an organization's API keys are not for browsers");

        // Caches must not hand one origin's answer to another.
        preflight.Headers.Vary.ShouldContain("Origin");
        otherApp.Headers.Vary.ShouldContain("Origin");
    }

    private Task<string> CreateSpaAsync(string? scope = null) =>
        server.WithServicesAsync(async services =>
        {
            var origin = $"https://{Guid.NewGuid():N}.example.com";
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = $"spa-{Guid.NewGuid():N}",
                ClientType = ClientTypes.Public,
                RedirectUris = { new Uri($"{origin}/callback") },
                Permissions = { Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.GrantTypes.AuthorizationCode, Permissions.ResponseTypes.Code },
            };
            if (scope is not null)
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
            }

            await services.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(descriptor);
            return origin;
        });

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path, string origin, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", origin);
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return await http.SendAsync(request, CancellationToken);
    }

    private static string? AllowedOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;
}
