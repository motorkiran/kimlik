using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Organizations;

/// <summary>Signing in to an organization: tokens carry its context, and refreshes check and switch it.</summary>
public sealed class OrganizationContextTests(KimlikServerFixture server)
{
    [Fact]
    public async Task SignIn_ToAnOrganization_CarriesItsRolesAndPermissions()
    {
        var user = await server.CreateUserAsync();
        var (globalRole, globalPermissions) = await server.CreateRoleAsync();
        await server.AssignToUserAsync(user.Id, globalRole);
        var organization = await server.CreateOrganizationAsync(user.Id);

        var tokens = await SignInAsync(user, await server.CreateWebClientAsync(), organization.Slug);

        var access = Payload(tokens.GetProperty("access_token").GetString()!);
        access.GetProperty(KimlikClaimTypes.OrganizationId).GetString().ShouldBe(organization.Id.ToString());
        Strings(access, KimlikClaimTypes.OrganizationRoles).ShouldBe([organization.Role]);
        Strings(access, KimlikClaimTypes.Roles).ShouldBe([globalRole]);
        Strings(access, KimlikClaimTypes.Permissions).ShouldBe([.. globalPermissions, organization.Permission], ignoreOrder: true);
        Payload(tokens.GetProperty("id_token").GetString()!).GetProperty(KimlikClaimTypes.OrganizationId).GetString().ShouldBe(organization.Id.ToString());
    }

    [Fact]
    public async Task SignIn_WithoutAnOrganization_HasNoOrganizationClaims()
    {
        var user = await server.CreateUserAsync();
        await server.CreateOrganizationAsync(user.Id);

        var tokens = await SignInAsync(user, await server.CreateWebClientAsync(), organization: null);

        Payload(tokens.GetProperty("access_token").GetString()!).TryGetProperty(KimlikClaimTypes.OrganizationId, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SignIn_ToAnOrganizationTheUserDoesNotBelongTo_IsDenied()
    {
        var user = await server.CreateUserAsync();
        var otherOrganization = await server.CreateOrganizationAsync();
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        using var callback = await browser.GetAsync(new AuthorizationRequest(client.ClientId) { Organization = otherOrganization.Slug }.Url);

        AuthorizationRequest.ReadCallback(callback)["error"].ShouldBe("access_denied");
    }

    [Fact]
    public async Task Refresh_KeepsTheOrganization_UntilTheMembershipEnds()
    {
        var user = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(user.Id);
        var client = await server.CreateWebClientAsync();
        var tokens = await SignInAsync(user, client, organization.Id.ToString());
        using var http = server.CreateClient();

        using var refreshed = await OidcFlows.RefreshAsync(http, client, tokens.GetProperty("refresh_token").GetString()!);
        var renewed = await refreshed.ReadJsonAsync();
        Payload(renewed.GetProperty("access_token").GetString()!).GetProperty(KimlikClaimTypes.OrganizationId).GetString().ShouldBe(organization.Id.ToString());

        await server.RemoveMemberAsync(organization.Id, user.Id);
        using var refused = await OidcFlows.RefreshAsync(http, client, renewed.GetProperty("refresh_token").GetString()!);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task Refresh_SwitchesToAnotherOrganizationOfTheUser()
    {
        var user = await server.CreateUserAsync();
        var first = await server.CreateOrganizationAsync(user.Id);
        var second = await server.CreateOrganizationAsync(user.Id);
        var foreign = await server.CreateOrganizationAsync();
        var client = await server.CreateWebClientAsync();
        var tokens = await SignInAsync(user, client, first.Slug);
        using var http = server.CreateClient();

        using var switched = await RefreshInAsync(http, client, tokens.GetProperty("refresh_token").GetString()!, second.Slug);
        var renewed = await switched.ReadJsonAsync();
        var access = Payload(renewed.GetProperty("access_token").GetString()!);
        access.GetProperty(KimlikClaimTypes.OrganizationId).GetString().ShouldBe(second.Id.ToString());
        Strings(access, KimlikClaimTypes.Permissions).ShouldBe([second.Permission]);

        using var refused = await RefreshInAsync(http, client, renewed.GetProperty("refresh_token").GetString()!, foreign.Slug);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ClientThatRequiresAnOrganization_LetsTheUserChooseOne()
    {
        var user = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(user.Id);
        var client = await CreateClientRequiringAnOrganizationAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client.ClientId);

        using var toPicker = await browser.GetAsync(request.Url);
        var picker = await browser.GetPageAsync(toPicker.Headers.Location!.OriginalString);
        picker.Text.ShouldContain("Which one do you want to continue with?");

        using var chosen = await browser.SubmitAsync(picker, submitter: ("organizationId", organization.Id.ToString()));
        using var callback = await browser.FollowAsync(await browser.GetAsync(chosen.Headers.Location!.OriginalString));
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Payload(tokens.GetProperty("access_token").GetString()!).GetProperty(KimlikClaimTypes.OrganizationId).GetString().ShouldBe(organization.Id.ToString());
    }

    [Fact]
    public async Task ClientThatRequiresAnOrganization_CannotSkipTheChoiceSilently()
    {
        var user = await server.CreateUserAsync();
        var client = await CreateClientRequiringAnOrganizationAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        using var callback = await browser.GetAsync(new AuthorizationRequest(client.ClientId) { Prompt = "none" }.Url);

        AuthorizationRequest.ReadCallback(callback)["error"].ShouldBe("interaction_required");
    }

    private async Task<JsonElement> SignInAsync(TestUser user, TestWebClient client, string? organization)
    {
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        var request = new AuthorizationRequest(client.ClientId) { Organization = organization };
        using var callback = await browser.GetAsync(request.Url);
        return await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);
    }

    private async Task<TestWebClient> CreateClientRequiringAnOrganizationAsync()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = $"workspace-{Guid.NewGuid():N}",
            DisplayName = "Workspace",
            Type = ClientType.Spa,
            RedirectUris = [TestWebClient.RedirectUri],
            Scopes = ["openid", "profile", "email", "offline_access", TestClients.ApiScope],
            RequireOrganization = true,
        });
        var client = (await created.ReadAsync<CreatedClientResponse>()).Client;
        client.RequireOrganization.ShouldBeTrue();
        return new TestWebClient(client.ClientId);
    }

    private static Task<HttpResponseMessage> RefreshInAsync(HttpClient http, TestWebClient client, string refreshToken, string organization) =>
        http.PostFormAsync("/connect/token",
        [
            new("grant_type", "refresh_token"),
            new("client_id", client.ClientId),
            new("refresh_token", refreshToken),
            new(KimlikParameters.Organization, organization),
        ]);

    private static JsonElement Payload(string jwt) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]))).RootElement.Clone();

    private static string[] Strings(JsonElement payload, string claim) =>
        [.. payload.GetProperty(claim).EnumerateArray().Select(value => value.GetString()!)];
}
