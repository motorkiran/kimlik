using System.Text;
using System.Text.Json;
using Kimlik.Contracts;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Oidc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

namespace Kimlik.Server.Tests.Access;

public sealed class AccessClaimsTests(KimlikServerFixture server)
{
    [Fact]
    public async Task UserToken_ForAnApplicationApi_CarriesRolesAndPermissions_ButNoSystemPermissions()
    {
        var (role, permissions) = await server.CreateRoleAsync();
        var user = await server.CreateUserAsync();
        await server.AssignToUserAsync(user.Id, role);
        await server.AssignToUserAsync(user.Id, SystemRoles.Admin);

        var payload = await AccessTokenPayloadAsync(user, $"openid {TestClients.ApiScope}");

        Strings(payload, KimlikClaimTypes.Roles).ShouldBe([role, SystemRoles.Admin], ignoreOrder: true);
        Strings(payload, KimlikClaimTypes.Permissions).ShouldBe(permissions, ignoreOrder: true);
    }

    [Fact]
    public async Task UserToken_ForKimlikApi_AlsoCarriesSystemPermissions()
    {
        var user = await server.CreateUserAsync();
        await server.AssignToUserAsync(user.Id, SystemRoles.Admin);

        var payload = await AccessTokenPayloadAsync(user, $"openid {KimlikScopes.Api}");

        Strings(payload, KimlikClaimTypes.Permissions).ShouldBe(SystemPermissions.Global.Keys, ignoreOrder: true);
    }

    [Fact]
    public async Task SingleRole_IsStillAJsonArray()
    {
        var (role, _) = await server.CreateRoleAsync();
        var user = await server.CreateUserAsync();
        await server.AssignToUserAsync(user.Id, role);

        var payload = await AccessTokenPayloadAsync(user, $"openid {TestClients.ApiScope}");

        payload.GetProperty(KimlikClaimTypes.Roles).ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task UserWithoutRoles_GetsNoAccessClaims()
    {
        var user = await server.CreateUserAsync();

        var payload = await AccessTokenPayloadAsync(user, $"openid {TestClients.ApiScope}");

        payload.TryGetProperty(KimlikClaimTypes.Roles, out _).ShouldBeFalse();
        payload.TryGetProperty(KimlikClaimTypes.Permissions, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ServiceClientToken_CarriesThePermissionsOfTheClientRoles()
    {
        var (role, permissions) = await server.CreateRoleAsync();
        var serviceClient = await server.CreateServiceClientAsync();
        await server.AssignToClientAsync(serviceClient.ClientId, role);
        using var client = server.CreateClient();

        var payload = Payload(await client.RequestClientCredentialsTokenAsync(serviceClient));

        Strings(payload, KimlikClaimTypes.Roles).ShouldBe([role]);
        Strings(payload, KimlikClaimTypes.Permissions).ShouldBe(permissions, ignoreOrder: true);
    }

    [Fact]
    public async Task SystemCatalog_IsInPlaceAfterStartup()
    {
        var adminPermissions = await server.QueryDatabaseAsync(context => context.Roles
            .Where(role => role.Key == SystemRoles.Admin)
            .SelectMany(role => role.Permissions)
            .Join(context.Permissions, link => link.PermissionId, permission => permission.Id, (_, permission) => permission.Key)
            .ToListAsync(TestContext.Current.CancellationToken));

        adminPermissions.ShouldBe(SystemPermissions.Global.Keys, ignoreOrder: true);
        (await server.WithServicesAsync(services => services.GetRequiredService<IOpenIddictScopeManager>().FindByNameAsync(KimlikScopes.Api).AsTask()))
            .ShouldNotBeNull();
    }

    private async Task<JsonElement> AccessTokenPayloadAsync(TestUser user, string scope)
    {
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        var request = new AuthorizationRequest(client.ClientId) { Scope = scope };
        using var callback = await browser.GetAsync(request.Url);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        return Payload(tokens.GetProperty("access_token").GetString()!);
    }

    private static JsonElement Payload(string jwt) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]))).RootElement.Clone();

    private static string[] Strings(JsonElement payload, string claim) =>
        [.. payload.GetProperty(claim).EnumerateArray().Select(value => value.GetString()!)];
}
