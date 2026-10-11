using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts;
using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Organizations;

/// <summary>Tokens for the <c>kimlik.organizations</c> scope, which Kimlik's organization components call the Account API with (design §12.7).</summary>
public sealed class OrganizationScopeTests(KimlikServerFixture server)
{
    private const string Scope = $"openid {KimlikScopes.Organizations}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Token_ManagesTheUsersOrganizations_WithoutSystemPermissions()
    {
        var user = await server.CreateUserAsync();
        await server.AssignToUserAsync(user.Id, SystemRoles.Admin);
        var organization = await server.CreateOrganizationAsync(user.Id);
        await server.SetMemberRolesAsync(organization.Id, user.Id, SystemRoles.OrganizationAdmin);
        var token = await TokenAsync(user);
        using var http = server.WithToken(token);

        var payload = Payload(token);
        payload.GetProperty("aud").GetString().ShouldBe(KimlikScopes.Organizations);
        payload.TryGetProperty(KimlikClaimTypes.Permissions, out _).ShouldBeFalse("an administrator's system permissions stay out of it");

        using var listed = await http.GetAsync("/api/v1/me/organizations", CancellationToken);
        (await listed.ReadAsync<List<MyOrganizationResponse>>()).ShouldContain(mine => mine.Id == organization.Id);
        using var roles = await http.GetAsync($"/api/v1/me/organizations/{organization.Id}/roles", CancellationToken);
        (await roles.ReadAsync<List<MyOrganizationRoleResponse>>()).Select(role => role.Key).ShouldContain(SystemRoles.OrganizationAdmin);
        using var invited = await http.PostJsonAsync(
            $"/api/v1/me/organizations/{organization.Id}/invitations", new CreateInvitationRequest { Email = $"grace-{Guid.NewGuid():N}@example.com" });
        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var invitations = await http.GetAsync("/api/v1/me/invitations", CancellationToken);
        invitations.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET", "/api/v1/me")]
    [InlineData("POST", "/api/v1/me/password")]
    [InlineData("POST", "/api/v1/me/delete")]
    [InlineData("GET", "/api/v1/me/organizations/{0}/api-keys")]
    [InlineData("GET", "/api/v1/users")]
    public async Task Token_ReachesNothingElse(string method, string path)
    {
        var user = await server.CreateUserAsync();
        await server.AssignToUserAsync(user.Id, SystemRoles.Admin);
        var organization = await server.CreateOrganizationAsync(user.Id);
        await server.SetMemberRolesAsync(organization.Id, user.Id, SystemRoles.OrganizationAdmin);
        using var http = server.WithToken(await TokenAsync(user));

        using var response = await http.SendJsonAsync(new HttpMethod(method), string.Format(System.Globalization.CultureInfo.InvariantCulture, path, organization.Id), "{}");

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task KimlikScopes_AreReservedForKimlik()
    {
        using var api = await server.CreateApiClientAsync();

        using var scope = await api.Http.PostJsonAsync("/api/v1/api-resources", new CreateApiResourceRequest { Scope = $"kimlik.{Guid.NewGuid():N}"[..20] });
        (await scope.ReadProblemCodeAsync()).ShouldBe("api_resource.reserved_scope");
        using var audience = await api.Http.PostJsonAsync(
            "/api/v1/api-resources", new CreateApiResourceRequest { Scope = $"shipping-{Guid.NewGuid():N}", Audience = KimlikScopes.Organizations });
        (await audience.ReadProblemCodeAsync()).ShouldBe("api_resource.reserved_audience");
    }

    private async Task<string> TokenAsync(TestUser user)
    {
        var (browser, _, tokens) = await server.SignInAndRedeemAsync(user, Scope);
        browser.Dispose();
        return tokens.GetProperty("access_token").GetString()!;
    }

    private static JsonElement Payload(string token) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]))).RootElement.Clone();
}
