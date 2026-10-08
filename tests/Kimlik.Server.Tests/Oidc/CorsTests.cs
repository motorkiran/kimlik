using System.Net;
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

    private Task<string> CreateSpaAsync() =>
        server.WithServicesAsync(async services =>
        {
            var origin = $"https://{Guid.NewGuid():N}.example.com";
            await services.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = $"spa-{Guid.NewGuid():N}",
                ClientType = ClientTypes.Public,
                RedirectUris = { new Uri($"{origin}/callback") },
                Permissions = { Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.GrantTypes.AuthorizationCode, Permissions.ResponseTypes.Code },
            });

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
