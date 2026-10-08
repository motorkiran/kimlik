using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Kimlik.AspNetCore;
using Kimlik.AspNetCore.Authorization;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Sdk;

/// <summary>An API protected with Kimlik.AspNetCore, validating tokens from the Kimlik server under test.</summary>
public sealed class ResourceServerTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserToken_WithThePermission_IsAccepted_AndDescribesTheUser()
    {
        var (role, permissions) = await server.CreateRoleAsync();
        var user = await server.CreateUserAsync();
        await server.AssignToUserAsync(user.Id, role);
        await using var api = await StartApiAsync(requiredPermission: permissions[0]);

        using var http = WithToken(api, await AccessTokenAsync(user));
        using var orders = await http.GetAsync("/orders", CancellationToken);
        using var me = await http.GetAsync("/me", CancellationToken);

        orders.StatusCode.ShouldBe(HttpStatusCode.OK);
        var caller = await me.ReadJsonAsync();
        caller.GetProperty("userId").GetGuid().ShouldBe(user.Id);
        caller.GetProperty("email").GetString().ShouldBe(user.Email);
        caller.GetProperty("isServiceClient").GetBoolean().ShouldBeFalse();
        Strings(caller, "roles").ShouldBe([role]);
        Strings(caller, "permissions").ShouldBe(permissions, ignoreOrder: true);
    }

    [Fact]
    public async Task UserToken_WithoutThePermission_IsForbidden()
    {
        var user = await server.CreateUserAsync();
        await using var api = await StartApiAsync(requiredPermission: "orders:read");

        using var http = WithToken(api, await AccessTokenAsync(user));
        using var response = await http.GetAsync("/orders", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ServiceClientToken_DescribesTheClient()
    {
        var (role, permissions) = await server.CreateRoleAsync();
        var serviceClient = await server.CreateServiceClientAsync();
        await server.AssignToClientAsync(serviceClient.ClientId, role);
        await using var api = await StartApiAsync(requiredPermission: permissions[0]);
        using var kimlik = server.CreateClient();

        using var http = WithToken(api, await kimlik.RequestClientCredentialsTokenAsync(serviceClient));
        using var orders = await http.GetAsync("/orders", CancellationToken);
        using var me = await http.GetAsync("/me", CancellationToken);

        orders.StatusCode.ShouldBe(HttpStatusCode.OK);
        var caller = await me.ReadJsonAsync();
        caller.GetProperty("isServiceClient").GetBoolean().ShouldBeTrue();
        caller.GetProperty("clientId").GetString().ShouldBe(serviceClient.ClientId);
        caller.GetProperty("userId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task RequestWithoutToken_IsUnauthorized()
    {
        await using var api = await StartApiAsync(requiredPermission: "orders:read");

        using var response = await api.GetTestClient().GetAsync("/orders", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenForAnotherApi_IsUnauthorized()
    {
        await using var api = await StartApiAsync(requiredPermission: "orders:read");
        using var kimlikApi = await server.CreateApiClientAsync();

        using var http = WithToken(api, kimlikApi.Http.DefaultRequestHeaders.Authorization!.Parameter!);
        using var response = await http.GetAsync("/orders", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IdentityToken_IsNotAnAccessToken()
    {
        var user = await server.CreateUserAsync();
        var (browser, client, tokens) = await server.SignInAndRedeemAsync(user);
        browser.Dispose();

        // The identity token's audience is the client, so the API expects that audience to rule everything else out.
        await using var api = await StartApiAsync(requiredPermission: null, audience: client.ClientId);
        using var http = WithToken(api, tokens.GetProperty("id_token").GetString()!);
        using var response = await http.GetAsync("/me", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<WebApplication> StartApiAsync(string? requiredPermission, string audience = TestClients.ApiResource)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddKimlik(options =>
        {
            options.Authority = new Uri(TestConfiguration.PublicUrl);
            options.Audience = audience;
            options.RequireHttpsMetadata = false;
        });

        // Kimlik runs in memory, so its discovery document and keys are fetched through the test server.
        builder.Services.Configure<JwtBearerOptions>(KimlikDefaults.AuthenticationScheme, options => options.BackchannelHttpHandler = server.Server.CreateHandler());

        var app = builder.Build();
        app.MapGet("/orders", () => "orders").RequirePermission(requiredPermission ?? "orders:read");
        app.MapGet("/me", (KimlikUser user) => new
        {
            user.Subject,
            user.ClientId,
            user.IsServiceClient,
            user.UserId,
            user.Email,
            user.Roles,
            user.Permissions,
        }).RequireAuthorization();

        await app.StartAsync(CancellationToken);
        return app;
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

    private static string[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(value => value.GetString()!)];
}
