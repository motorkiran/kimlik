using System.Net;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Microsoft.AspNetCore.WebUtilities;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>Pushed authorization requests (RFC 9126): the client posts the parameters, the browser carries a reference.</summary>
public sealed class PushedAuthorizationTests(KimlikServerFixture server)
{
    [Fact]
    public async Task PushedRequest_SignsTheUserIn_AndAClientThatRequiresItRefusesOthers()
    {
        var client = await CreateSpaAsync(requirePushedAuthorization: true);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client.ClientId) { Scope = "openid profile" };

        using var plain = await browser.GetAsync(request.Url);
        var refused = plain.Headers.Location is { } location ? QueryHelpers.ParseQuery(location.Query) : [];
        refused.ShouldNotContainKey("code");

        using var callback = await browser.GetAsync(await PushAsync(browser.Client, request));
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);
        tokens.GetProperty("id_token").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task PushedRequest_ForAFreshSignIn_AsksOnlyOnce_ThenForConsent()
    {
        var client = await CreateSpaAsync(requirePushedAuthorization: false, firstParty: false);
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client.ClientId) { Scope = "openid", Prompt = "login" };

        using var challenge = await browser.GetAsync(await PushAsync(browser.Client, request));
        challenge.Headers.Location!.AbsolutePath.ShouldBe("/signin");
        var returnUrl = QueryHelpers.ParseQuery(challenge.Headers.Location.Query)["ReturnUrl"].ToString();
        using var signedInAgain = await browser.SignInAsync(user.Email, user.Password, returnUrl);
        var consent = await browser.GetPageAsync(signedInAgain.Headers.Location!.OriginalString);
        using var callback = await browser.SubmitAsync(consent, submitter: ("consent", "accept"));

        AuthorizationRequest.ReadCallback(callback).ShouldContainKey("code");
    }

    [Fact]
    public async Task Discovery_AdvertisesTheEndpoint()
    {
        using var http = server.CreateClient();

        using var response = await http.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        var configuration = await response.ReadJsonAsync();

        configuration.GetProperty("pushed_authorization_request_endpoint").GetString().ShouldEndWith("/connect/par");
    }

    /// <summary>Pushes the request and returns the authorization URL that refers to it.</summary>
    private static async Task<string> PushAsync(HttpClient http, AuthorizationRequest request)
    {
        using var pushed = await http.PostFormAsync("/connect/par", [.. request.Parameters.Select(parameter => new KeyValuePair<string, string>(parameter.Key, parameter.Value!))]);
        var body = await pushed.ReadJsonAsync();
        pushed.StatusCode.ShouldBe(HttpStatusCode.Created, body.ToString());
        body.GetProperty("expires_in").GetInt32().ShouldBePositive();

        return QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = request.Parameters["client_id"],
            ["request_uri"] = body.GetProperty("request_uri").GetString(),
        });
    }

    private async Task<TestWebClient> CreateSpaAsync(bool requirePushedAuthorization, bool firstParty = true)
    {
        using var api = await server.CreateApiClientAsync();
        var clientId = $"spa-{Guid.NewGuid():N}"[..24];
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = clientId,
            DisplayName = "Orders web app",
            Type = ClientType.Spa,
            RedirectUris = [TestWebClient.RedirectUri],
            Scopes = ["openid", "profile"],
            RequirePushedAuthorization = requirePushedAuthorization,
            FirstParty = firstParty,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await created.ReadAsync<CreatedClientResponse>()).Client.RequirePushedAuthorization.ShouldBe(requirePushedAuthorization);
        return new TestWebClient(clientId);
    }
}
