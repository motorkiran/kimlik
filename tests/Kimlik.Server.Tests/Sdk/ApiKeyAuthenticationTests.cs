using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Kimlik.AspNetCore;
using Kimlik.AspNetCore.Authorization;
using Kimlik.Client;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Organizations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Tests.Sdk;

/// <summary>An API protected with Kimlik.AspNetCore that accepts API keys next to access tokens.</summary>
public sealed class ApiKeyAuthenticationTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UsersKey_IsAccepted_LikeTheirAccessToken()
    {
        var (role, permissions) = await server.CreateRoleAsync();
        var user = await server.CreateUserAsync();
        await server.AssignToUserAsync(user.Id, role);
        var key = await CreateKeyAsync(user, permissions[0]);
        await using var api = await StartApiAsync(permissions);

        using var http = WithKey(api, key.Key);
        using var read = await http.GetAsync("/read", CancellationToken);
        using var write = await http.GetAsync("/write", CancellationToken);
        var caller = await (await http.GetAsync("/me", CancellationToken)).ReadJsonAsync();

        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        caller.GetProperty("userId").GetGuid().ShouldBe(user.Id);
        caller.GetProperty("apiKeyId").GetGuid().ShouldBe(key.ApiKey.Id);
        caller.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()).ShouldBe([permissions[0]]);
    }

    [Fact]
    public async Task OrganizationsKey_ActsOnItsOwnBehalf()
    {
        var admin = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(admin.Id);
        await server.SetMemberRolesAsync(organization.Id, admin.Id, organization.Role, SystemRoles.OrganizationAdmin);
        using var me = server.WithToken(await server.UserAccessTokenAsync(admin));
        using var response = await me.PostJsonAsync(
            $"/api/v1/me/organizations/{organization.Id}/api-keys", new CreateApiKeyRequest { Name = "CI", Permissions = [organization.Permission] });
        var created = await response.ReadAsync<CreatedApiKeyResponse>();
        await using var api = await StartApiAsync([organization.Permission]);

        using var http = WithKey(api, created.Key);
        using var read = await http.GetAsync("/read", CancellationToken);
        var caller = await (await http.GetAsync("/me", CancellationToken)).ReadJsonAsync();

        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        caller.GetProperty("userId").ValueKind.ShouldBe(JsonValueKind.Null);
        caller.GetProperty("organizationId").GetGuid().ShouldBe(organization.Id);
        caller.GetProperty("subject").GetString().ShouldBe(created.ApiKey.Id.ToString());
    }

    [Fact]
    public async Task RevokedOrUnknownKeys_AreUnauthorized()
    {
        var user = await server.CreateUserAsync();
        var key = await CreateKeyAsync(user);
        await using var api = await StartApiAsync([], cacheDuration: TimeSpan.Zero);
        (await WithKey(api, key.Key).GetAsync("/me", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var admin = await server.CreateApiClientAsync();
        using var revoked = await admin.Http.DeleteAsync($"/api/v1/api-keys/{key.ApiKey.Id}", CancellationToken);

        (await WithKey(api, key.Key).GetAsync("/me", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await WithKey(api, "kmk_unknown").GetAsync("/me", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Keys_AreRefused_UnlessEnabled()
    {
        var user = await server.CreateUserAsync();
        var key = await CreateKeyAsync(user);
        await using var api = await StartApiAsync([], enabled: false);

        (await WithKey(api, key.Key).GetAsync("/me", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Keys_NeedKimlikClient()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddKimlik(options =>
        {
            options.Authority = new Uri(TestConfiguration.PublicUrl);
            options.Audience = TestClients.ApiResource;
            options.ApiKeys.Enabled = true;
        });
        await using var app = builder.Build();

        var failure = await Should.ThrowAsync<OptionsValidationException>(() => app.StartAsync(CancellationToken));
        failure.Message.ShouldContain("AddKimlikClient");
    }

    private async Task<CreatedApiKeyResponse> CreateKeyAsync(TestUser user, params string[] permissions)
    {
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));
        using var response = await me.PostJsonAsync("/api/v1/me/api-keys", new CreateApiKeyRequest { Name = "Key", Permissions = permissions });
        return await response.ReadAsync<CreatedApiKeyResponse>();
    }

    private async Task<WebApplication> StartApiAsync(string[] permissions, bool enabled = true, TimeSpan? cacheDuration = null)
    {
        var verifier = await server.CreateServiceClientAsync(KimlikScopes.Api);
        await server.AssignToClientAsync(verifier.ClientId, await server.CreateRoleWithAsync(SystemPermissions.ApiKeysVerify));

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddKimlik(options =>
        {
            options.Authority = new Uri(TestConfiguration.PublicUrl);
            options.Audience = TestClients.ApiResource;
            options.RequireHttpsMetadata = false;
            options.ApiKeys.Enabled = enabled;
            options.ApiKeys.CacheDuration = cacheDuration ?? options.ApiKeys.CacheDuration;
        });
        builder.Services.AddKimlikClient(options =>
        {
            options.Authority = new Uri(TestConfiguration.PublicUrl);
            options.ClientId = verifier.ClientId;
            options.ClientSecret = verifier.ClientSecret;
        });

        // Kimlik runs in memory, so every call to it goes through the test server.
        builder.Services.Configure<JwtBearerOptions>(KimlikDefaults.AuthenticationScheme, options => options.BackchannelHttpHandler = server.Server.CreateHandler());
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => server.Server.CreateHandler()));

        var app = builder.Build();
        app.MapGet("/read", () => "read").RequirePermission(permissions.ElementAtOrDefault(0) ?? "orders:read");
        app.MapGet("/write", () => "write").RequirePermission(permissions.ElementAtOrDefault(1) ?? "orders:write");
        app.MapGet("/me", (KimlikUser user) => new { user.Subject, user.UserId, user.ApiKeyId, user.OrganizationId, user.Permissions }).RequireAuthorization();

        await app.StartAsync(CancellationToken);
        return app;
    }

    private static HttpClient WithKey(WebApplication api, string key)
    {
        var http = api.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return http;
    }
}
