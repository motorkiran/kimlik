using System.Net;
using System.Text.Json;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Oidc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kimlik.Server.Tests.Api;

public sealed class ClientApiTests(KimlikServerFixture server)
{
    private const string Clients = "/api/v1/clients";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ServiceClient_GetsTokens_CarryingThePermissionsOfItsRoles()
    {
        using var api = await server.CreateApiClientAsync();
        var (role, permissions) = await server.CreateRoleAsync();

        using var created = await api.Http.PostJsonAsync(Clients, new CreateClientRequest
        {
            ClientId = NewClientId(),
            DisplayName = "Billing worker",
            Type = ClientType.Service,
            Scopes = [TestClients.ApiScope],
            Roles = [role],
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var (client, secret) = await created.ReadAsync<CreatedClientResponse>();
        created.Headers.Location!.OriginalString.ShouldBe($"{Clients}/{client.Id}");
        client.Type.ShouldBe(ClientType.Service);
        client.Scopes.ShouldBe([TestClients.ApiScope]);
        client.Roles.ShouldBe([role]);
        secret.ShouldNotBeNullOrEmpty();

        using var http = server.CreateClient();
        var token = new JsonWebToken(await http.RequestClientCredentialsTokenAsync(new TestClient(client.ClientId, secret)));
        token.Subject.ShouldBe(client.ClientId);
        JsonSerializer.Deserialize<string[]>(token.GetPayloadValue<JsonElement>(KimlikClaimTypes.Permissions).GetRawText()).ShouldBe(permissions, ignoreOrder: true);
    }

    [Fact]
    public async Task SpaClient_SignsUsersIn_WithAuthorizationCodeAndPkce()
    {
        using var api = await server.CreateApiClientAsync();

        using var created = await api.Http.PostJsonAsync(Clients, new CreateClientRequest
        {
            ClientId = NewClientId(),
            DisplayName = "Orders web app",
            Type = ClientType.Spa,
            RedirectUris = [TestWebClient.RedirectUri],
            Scopes = ["openid", "profile", "email", "offline_access", TestClients.ApiScope],
        });
        var (client, secret) = await created.ReadAsync<CreatedClientResponse>();
        secret.ShouldBeNull();
        client.FirstParty.ShouldBeTrue();

        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client.ClientId);
        using var callback = await browser.GetAsync(request.Url);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, new TestWebClient(client.ClientId), request, AuthorizationRequest.ReadCallback(callback)["code"]);

        tokens.GetProperty("refresh_token").GetString().ShouldNotBeNullOrEmpty();
        new JsonWebToken(tokens.GetProperty("access_token").GetString()).Audiences.ShouldBe([TestClients.ApiResource]);
    }

    [Fact]
    public async Task ThirdPartyClient_AsksForConsent()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync(Clients, new CreateClientRequest
        {
            ClientId = NewClientId(),
            DisplayName = "Partner app",
            Type = ClientType.Web,
            FirstParty = false,
            RedirectUris = [TestWebClient.RedirectUri],
            Scopes = ["openid", "profile", "email", "offline_access", TestClients.ApiScope],
        });
        var client = (await created.ReadAsync<CreatedClientResponse>()).Client;

        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var consentPage = await browser.GetPageAsync(new AuthorizationRequest(client.ClientId).Url);

        consentPage.Text.ShouldContain("Partner app");
    }

    [Fact]
    public async Task RegeneratedSecret_ReplacesThePreviousOne()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync(Clients, ServiceClientRequest());
        var (client, oldSecret) = await created.ReadAsync<CreatedClientResponse>();

        using var regenerated = await api.Http.PostAsync($"{Clients}/{client.Id}/secret");
        regenerated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var newSecret = (await regenerated.ReadAsync<ClientSecretResponse>()).ClientSecret;

        using var http = server.CreateClient();
        using var withOldSecret = await RequestTokenAsync(http, new TestClient(client.ClientId, oldSecret!));
        withOldSecret.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var withNewSecret = await RequestTokenAsync(http, new TestClient(client.ClientId, newSecret));
        withNewSecret.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PublicClient_HasNoSecret_ToRegenerate()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync(Clients, new CreateClientRequest
        {
            ClientId = NewClientId(),
            DisplayName = "Mobile app",
            Type = ClientType.Native,
            RedirectUris = ["com.example.app:/callback", "http://127.0.0.1/callback"],
        });
        var client = (await created.ReadAsync<CreatedClientResponse>()).Client;

        using var response = await api.Http.PostAsync($"{Clients}/{client.Id}/secret");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe("client.not_confidential");
    }

    [Theory]
    [InlineData("""{ "clientId": "my app", "displayName": "App", "type": "spa", "redirectUris": ["https://app.example.com/cb"] }""", "client.invalid_client_id")]
    [InlineData("""{ "clientId": "{id}", "displayName": "App", "type": "spa" }""", "client.redirect_uri_required")]
    [InlineData("""{ "clientId": "{id}", "displayName": "App", "type": "spa", "redirectUris": ["http://app.example.com/cb"] }""", "client.invalid_redirect_uri")]
    [InlineData("""{ "clientId": "{id}", "displayName": "App", "type": "spa", "redirectUris": ["https://app.example.com/cb#x"] }""", "client.invalid_redirect_uri")]
    [InlineData("""{ "clientId": "{id}", "displayName": "App", "type": "spa", "redirectUris": ["com.example.app:/cb"] }""", "client.invalid_redirect_uri")]
    [InlineData("""{ "clientId": "{id}", "displayName": "App", "type": "spa", "redirectUris": ["https://app.example.com/cb"], "scopes": ["nope"] }""", "client.unknown_scope")]
    [InlineData("""{ "clientId": "{id}", "displayName": "App", "type": "spa", "redirectUris": ["https://app.example.com/cb"], "roles": ["kimlik-admin"] }""", "client.roles_not_supported")]
    [InlineData("""{ "clientId": "{id}", "displayName": "App", "type": "service", "redirectUris": ["https://app.example.com/cb"] }""", "client.redirect_uris_not_supported")]
    [InlineData("""{ "clientId": "{id}", "displayName": "App", "type": "service", "scopes": ["openid"] }""", "client.user_scope_not_supported")]
    [InlineData("""{ "clientId": "{id}", "displayName": "App" }""", "request.invalid")]
    public async Task CreateClient_WithInvalidSettings_IsRejected(string json, string code)
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.SendJsonAsync(HttpMethod.Post, Clients, json.Replace("{id}", NewClientId(), StringComparison.Ordinal));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe(code);
    }

    [Fact]
    public async Task CreateClient_WithTakenClientId_IsConflict()
    {
        using var api = await server.CreateApiClientAsync();
        var request = ServiceClientRequest();
        using var first = await api.Http.PostJsonAsync(Clients, request);

        using var second = await api.Http.PostJsonAsync(Clients, request);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadProblemCodeAsync()).ShouldBe("client.already_exists");
    }

    [Fact]
    public async Task UpdateClient_ChangesWhatIsSent_AndKeepsTheRest()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync(Clients, new CreateClientRequest
        {
            ClientId = NewClientId(),
            DisplayName = "Orders web app",
            Type = ClientType.Spa,
            RedirectUris = ["https://app.example.com/callback"],
            Scopes = ["openid", TestClients.ApiScope],
        });
        var client = (await created.ReadAsync<CreatedClientResponse>()).Client;

        using var patched = await api.Http.SendJsonAsync(
            HttpMethod.Patch,
            $"{Clients}/{client.Id}",
            """{ "firstParty": false, "redirectUris": ["https://app.example.com/v2/callback"], "postLogoutRedirectUris": ["http://localhost:3000/"] }""");
        patched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await patched.ReadAsync<ClientResponse>();
        updated.FirstParty.ShouldBeFalse();
        updated.RedirectUris.ShouldBe(["https://app.example.com/v2/callback"]);
        updated.PostLogoutRedirectUris.ShouldBe(["http://localhost:3000/"]);
        updated.DisplayName.ShouldBe("Orders web app");
        updated.Scopes.ShouldBe(["openid", TestClients.ApiScope]);

        using var unnamed = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Clients}/{client.Id}", """{ "displayName": null }""");
        (await unnamed.ReadProblemCodeAsync()).ShouldBe("client.invalid_display_name");
    }

    [Fact]
    public async Task ClientRoles_StayWithinTheCallersOwnAccess()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync(Clients, ServiceClientRequest());
        var client = (await created.ReadAsync<CreatedClientResponse>()).Client;
        using var clientManager = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.ClientsRead, SystemPermissions.ClientsWrite));

        using var escalated = await clientManager.Http.PutJsonAsync($"{Clients}/{client.Id}/roles", new SetRolesRequest { Roles = [SystemRoles.Admin] });
        escalated.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await escalated.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");

        using var granted = await api.Http.PutJsonAsync($"{Clients}/{client.Id}/roles", new SetRolesRequest { Roles = [SystemRoles.Admin] });
        (await granted.ReadAsync<ClientResponse>()).Roles.ShouldBe([SystemRoles.Admin]);

        // Now that the client is an administrator, a client manager without that access cannot touch it.
        using var regenerated = await clientManager.Http.PostAsync($"{Clients}/{client.Id}/secret");
        using var deleted = await clientManager.Http.DeleteAsync($"{Clients}/{client.Id}", CancellationToken);
        regenerated.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deleted.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeletedClient_CanNoLongerGetTokens()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync(Clients, ServiceClientRequest());
        var (client, secret) = await created.ReadAsync<CreatedClientResponse>();

        using var deleted = await api.Http.DeleteAsync($"{Clients}/{client.Id}", CancellationToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var http = server.CreateClient();
        using var refused = await RequestTokenAsync(http, new TestClient(client.ClientId, secret!));
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var gone = await api.Http.GetAsync($"{Clients}/{client.Id}", CancellationToken);
        (await gone.ReadProblemCodeAsync()).ShouldBe("client.not_found");
    }

    private static CreateClientRequest ServiceClientRequest() => new()
    {
        ClientId = NewClientId(),
        DisplayName = "Billing worker",
        Type = ClientType.Service,
        Scopes = [TestClients.ApiScope],
    };

    private static Task<HttpResponseMessage> RequestTokenAsync(HttpClient http, TestClient client) =>
        http.PostFormAsync("/connect/token", [new("grant_type", "client_credentials"), new("scope", TestClients.ApiScope)], client);

    [Fact]
    public async Task AppsThatSignUsersInToKimliksApi_TakeFullAccessToSetUp()
    {
        using var delegated = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.ClientsRead, SystemPermissions.ClientsWrite));
        using var admin = await server.CreateApiClientAsync();
        var request = new CreateClientRequest
        {
            ClientId = NewClientId(),
            DisplayName = "Console",
            Type = ClientType.Spa,
            RedirectUris = ["https://console.example.com/callback"],
            Scopes = ["openid", KimlikScopes.Api],
        };

        // Its tokens would carry the access of whoever signs in, administrators included.
        using var refused = await delegated.Http.PostJsonAsync(Clients, request);
        (await refused.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");

        using var created = await admin.Http.PostJsonAsync(Clients, request);
        var client = (await created.ReadAsync<CreatedClientResponse>()).Client;

        using var redirected = await delegated.Http.SendJsonAsync(
            HttpMethod.Patch, $"{Clients}/{client.Id}", """{ "redirectUris": ["https://attacker.example.com/callback"] }""");
        (await redirected.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");
    }

    [Fact]
    public async Task FirstPartyApp_AsksForConsent_TheFirstTimeItWantsKimliksApi()
    {
        var user = await server.CreateUserAsync();
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var _ = await browser.SignInAsync(user.Email, user.Password);

        var consent = await browser.GetPageAsync(new AuthorizationRequest(client.ClientId) { Scope = $"openid {KimlikScopes.Api}" }.Url);
        using var accepted = await browser.SubmitAsync(consent, submitter: ("consent", "accept"));
        accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        // Once given, the consent stands; other scopes never asked for it.
        var request = new AuthorizationRequest(client.ClientId) { Scope = $"openid {KimlikScopes.Api}" };
        using var again = await browser.GetAsync(request.Url);
        AuthorizationRequest.ReadCallback(again).ShouldContainKey("code");
    }

    private static string NewClientId() => $"client-{Guid.NewGuid():N}";
}
