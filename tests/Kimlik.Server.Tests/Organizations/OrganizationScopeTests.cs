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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

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
    public async Task FirstPartyApp_AsksOnce_BeforeItManagesTheUsersOrganizations()
    {
        var user = await server.CreateUserAsync();
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        using var first = await browser.GetAsync(new AuthorizationRequest(client.ClientId) { Scope = Scope }.Url);
        var consent = await Browser.ReadPageAsync(first);
        consent.Text.ShouldContain("Manage your organizations");
        using var accepted = await browser.SubmitAsync(consent, submitter: ("consent", "accept"));
        AuthorizationRequest.ReadCallback(accepted).ShouldContainKey("code");

        using var again = await browser.GetAsync(new AuthorizationRequest(client.ClientId) { Scope = Scope }.Url);
        AuthorizationRequest.ReadCallback(again).ShouldContainKey("code");
    }

    [Fact]
    public async Task ScopeTakenFromTheApp_EndsTheSignInsThatHadIt()
    {
        var user = await server.CreateUserAsync();
        var (browser, client, tokens) = await server.SignInAndRedeemAsync(user, $"openid offline_access {KimlikScopes.Organizations}");
        using var _ = browser;
        await server.WithServicesAsync(async services =>
        {
            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            var application = (await applications.FindByClientIdAsync(client.ClientId))!;
            var descriptor = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(descriptor, application);
            descriptor.Permissions.Remove(OpenIddictConstants.Permissions.Prefixes.Scope + KimlikScopes.Organizations).ShouldBeTrue();
            await applications.UpdateAsync(application, descriptor);
        });

        using var refreshed = await OidcFlows.RefreshAsync(browser.Client, client, tokens.GetProperty("refresh_token").GetString()!);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refreshed.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task LastManager_CannotGiveUpTheirOwnSayOverTheMembers()
    {
        var user = await server.CreateUserAsync();
        var colleague = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(user.Id, colleague.Id);
        await server.SetMemberRolesAsync(organization.Id, user.Id, SystemRoles.OrganizationAdmin);
        using var http = server.WithToken(await TokenAsync(user));
        var roles = $"/api/v1/me/organizations/{organization.Id}/members/{user.Id}/roles";

        using var orphaned = await http.PutJsonAsync(roles, new SetRolesRequest { Roles = [organization.Role] });
        orphaned.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await orphaned.ReadProblemCodeAsync()).ShouldBe("organization.last_administrator");

        await server.SetMemberRolesAsync(organization.Id, colleague.Id, SystemRoles.OrganizationAdmin);
        using var handedOver = await http.PutJsonAsync(roles, new SetRolesRequest { Roles = [organization.Role] });
        handedOver.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invitations_AreListedByStatus()
    {
        var user = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(user.Id);
        await server.SetMemberRolesAsync(organization.Id, user.Id, SystemRoles.OrganizationAdmin);
        using var http = server.WithToken(await TokenAsync(user));
        var invitations = $"/api/v1/me/organizations/{organization.Id}/invitations";
        using var revokedOne = await http.PostJsonAsync(invitations, new CreateInvitationRequest { Email = $"gone-{Guid.NewGuid():N}@example.com" });
        using var revoke = await http.DeleteAsync($"{invitations}/{(await revokedOne.ReadAsync<InvitationResponse>()).Id}", CancellationToken);
        using var open = await http.PostJsonAsync(invitations, new CreateInvitationRequest { Email = $"open-{Guid.NewGuid():N}@example.com" });
        var pending = await open.ReadAsync<InvitationResponse>();

        using var listed = await http.GetAsync($"{invitations}?status=pending", CancellationToken);
        using var all = await http.GetAsync(invitations, CancellationToken);

        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync(CancellationToken));
        (await listed.ReadAsync<Page<InvitationResponse>>()).Items.Select(invitation => invitation.Id).ShouldBe([pending.Id]);
        (await all.ReadAsync<Page<InvitationResponse>>()).Items.Count.ShouldBe(2);
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
        using var client = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest { ClientId = KimlikScopes.Organizations, DisplayName = "Not Kimlik", Type = ClientType.Service });
        (await client.ReadProblemCodeAsync()).ShouldBe("client.invalid_client_id");
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
