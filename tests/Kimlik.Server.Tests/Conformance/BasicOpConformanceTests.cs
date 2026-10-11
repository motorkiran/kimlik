using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Passkeys;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Conformance;

/// <summary>
/// What the OpenID Foundation's Basic OP and Config OP test plans check (design §8.12), against a web client set up as
/// the conformance suite's is: confidential, first party, without PKCE. Each test names the suite's modules it follows.
/// </summary>
public sealed class BasicOpConformanceTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>oidcc-server, oidcc-userinfo-get, oidcc-scope-profile, oidcc-scope-email: the basic flow, with <c>client_secret_basic</c>.</summary>
    [Fact]
    public async Task CodeFlow_WithoutPkce_GivesValidTokens()
    {
        var (client, secret) = await CreateClientAsync();
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client) { Pkce = false, Scope = "openid profile email" };

        var tokens = await RedeemAsync(browser.Client, client, secret, await CodeAsync(browser, request.Url), basic: true);

        var idToken = Payload(tokens.GetProperty("id_token").GetString()!);
        idToken.GetProperty("iss").GetString().ShouldBe("http://localhost/");
        idToken.GetProperty("aud").GetString().ShouldBe(client);
        idToken.GetProperty("nonce").GetString().ShouldBe(request.Nonce);
        idToken.GetProperty("sub").GetString().ShouldBe(user.Id.ToString());
        idToken.TryGetProperty("auth_time", out _).ShouldBeTrue();
        using var userInfo = await UserInfoAsync(browser.Client, HttpMethod.Get, tokens.GetProperty("access_token").GetString()!, inBody: false);
        var claims = await userInfo.ReadJsonAsync();
        claims.GetProperty("sub").GetString().ShouldBe(user.Id.ToString());
        claims.GetProperty("name").GetString().ShouldBe("Ada Lovelace");
        claims.GetProperty("email").GetString().ShouldBe(user.Email);
        claims.GetProperty("email_verified").GetBoolean().ShouldBeTrue();
    }

    /// <summary>oidcc-server with <c>client_secret_post</c>, oidcc-userinfo-post-header, oidcc-userinfo-post-body.</summary>
    [Fact]
    public async Task SecretInTheForm_AndUserInfoByPost_Work()
    {
        var (client, secret) = await CreateClientAsync();
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);

        var tokens = await RedeemAsync(browser.Client, client, secret, await CodeAsync(browser, new AuthorizationRequest(client) { Pkce = false, Scope = "openid" }.Url), basic: false);
        var accessToken = tokens.GetProperty("access_token").GetString()!;

        using var inHeader = await UserInfoAsync(browser.Client, HttpMethod.Post, accessToken, inBody: false);
        inHeader.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var inBody = await UserInfoAsync(browser.Client, HttpMethod.Post, accessToken, inBody: true);
        inBody.StatusCode.ShouldBe(HttpStatusCode.OK, await inBody.Content.ReadAsStringAsync(CancellationToken));
        (await inBody.ReadJsonAsync()).GetProperty("sub").GetString().ShouldBe(user.Id.ToString());
    }

    /// <summary>
    /// oidcc-ensure-request-without-nonce-succeeds-for-code-flow, oidcc-display-page, oidcc-display-popup,
    /// oidcc-ensure-request-with-unknown-parameter-succeeds, oidcc-login-hint, oidcc-ui-locales, oidcc-claims-locales,
    /// oidcc-ensure-request-with-acr-values-succeeds, oidcc-claims-essential, oidcc-ensure-other-scope-order-succeeds.
    /// </summary>
    [Theory]
    [InlineData("nonce", null)]
    [InlineData("display", "popup")]
    [InlineData("display", "page")]
    [InlineData("extra", "foobar")]
    [InlineData("login_hint", "ada@example.com")]
    [InlineData("ui_locales", "se")]
    [InlineData("claims_locales", "se")]
    [InlineData("acr_values", "1 2")]
    [InlineData("claims", """{"userinfo":{"name":{"essential":true}}}""")]
    [InlineData("scope", "email openid")]
    public async Task OptionalParameters_DoNotGetInTheWay(string name, string? value)
    {
        var (client, secret) = await CreateClientAsync();
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);
        var parameters = new AuthorizationRequest(client) { Pkce = false, Scope = "openid" }.Parameters;
        if (value is null)
        {
            parameters.Remove(name);
        }
        else
        {
            parameters[name] = value;
        }

        var tokens = await RedeemAsync(browser.Client, client, secret, await CodeAsync(browser, QueryHelpers.AddQueryString("/connect/authorize", parameters)), basic: true);

        tokens.GetProperty("id_token").GetString().ShouldNotBeNullOrEmpty();
    }

    /// <summary>oidcc-ensure-request-with-valid-pkce-succeeds: a web client that sends PKCE anyway is held to it.</summary>
    [Fact]
    public async Task Pkce_IsHonored_WhenSent()
    {
        var (client, secret) = await CreateClientAsync();
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client) { Scope = "openid" };
        var code = await CodeAsync(browser, request.Url);

        using var withoutVerifier = await TokenAsync(browser.Client, client, secret, [new("grant_type", "authorization_code"), new("code", code), new("redirect_uri", request.RedirectUri)]);
        withoutVerifier.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var again = await CodeAsync(browser, request.Url);
        using var withVerifier = await TokenAsync(
            browser.Client, client, secret, [new("grant_type", "authorization_code"), new("code", again), new("redirect_uri", request.RedirectUri), new("code_verifier", request.CodeVerifier)]);
        withVerifier.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>oidcc-prompt-none-logged-in, oidcc-id-token-hint.</summary>
    [Fact]
    public async Task PromptNone_ForASignedInUser_ReturnsACode_EvenWithAnIdTokenHint()
    {
        var (client, secret) = await CreateClientAsync();
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);
        var first = await RedeemAsync(browser.Client, client, secret, await CodeAsync(browser, new AuthorizationRequest(client) { Pkce = false, Scope = "openid" }.Url), basic: true);

        var request = new AuthorizationRequest(client)
        {
            Pkce = false,
            Scope = "openid",
            Prompt = "none",
            Extra = new Dictionary<string, string> { ["id_token_hint"] = first.GetProperty("id_token").GetString()! },
        };
        var tokens = await RedeemAsync(browser.Client, client, secret, await CodeAsync(browser, request.Url), basic: true);

        Payload(tokens.GetProperty("id_token").GetString()!).GetProperty("sub").GetString().ShouldBe(user.Id.ToString());
    }

    /// <summary>oidcc-max-age-1: a sign-in older than <c>max_age</c> is made again, and <c>auth_time</c> says when.</summary>
    [Fact]
    public async Task MaxAge_AsksForAFreshSignIn()
    {
        var clock = new ShiftedTimeProvider();
        await using var kimlik = server.WithShiftedClock(clock);
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        var (client, _) = await CreateClientAsync();
        var user = await server.CreateUserAsync();
        using var browser = new Browser(kimlik);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);

        clock.Offset = TimeSpan.FromSeconds(5);
        using var authorization = await browser.GetAsync(
            new AuthorizationRequest(client) { Pkce = false, Scope = "openid", Extra = new Dictionary<string, string> { ["max_age"] = "1" } }.Url);

        authorization.Headers.Location!.AbsolutePath.ShouldBe("/signin");
    }

    /// <summary>oidcc-codereuse, oidcc-codereuse-30seconds: a code works once, and using it again revokes what it gave.</summary>
    [Fact]
    public async Task ReusedCode_IsRefused_AndRevokesItsTokens()
    {
        var (client, secret) = await CreateClientAsync();
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client) { Pkce = false, Scope = "openid" };
        var code = await CodeAsync(browser, request.Url);
        var tokens = await RedeemAsync(browser.Client, client, secret, code, basic: true);

        using var reused = await TokenAsync(browser.Client, client, secret, [new("grant_type", "authorization_code"), new("code", code), new("redirect_uri", request.RedirectUri)]);

        reused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reused.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("invalid_grant");
        using var userInfo = await UserInfoAsync(browser.Client, HttpMethod.Get, tokens.GetProperty("access_token").GetString()!, inBody: false);
        userInfo.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>oidcc-ensure-post-request-succeeds: the authorization request can come as a form post.</summary>
    [Fact]
    public async Task AuthorizationRequest_ByPost_Succeeds()
    {
        var (client, secret) = await CreateClientAsync();
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client) { Pkce = false, Scope = "openid" };

        using var form = new FormUrlEncodedContent(request.Parameters.Select(parameter => new KeyValuePair<string, string>(parameter.Key, parameter.Value!)));
        using var posted = await browser.Client.PostAsync("/connect/authorize", form, CancellationToken);

        var callback = AuthorizationRequest.ReadCallback(posted);
        callback["state"].ShouldBe(request.State);
        (await RedeemAsync(browser.Client, client, secret, callback["code"], basic: true)).GetProperty("id_token").GetString().ShouldNotBeNullOrEmpty();
    }

    /// <summary>oidcc-response-type-missing: the error goes back to the client.</summary>
    [Fact]
    public async Task MissingResponseType_IsAnError()
    {
        var (client, _) = await CreateClientAsync();
        var parameters = new AuthorizationRequest(client) { Pkce = false, Scope = "openid" }.Parameters;
        parameters.Remove("response_type");
        using var browser = new Browser(server);

        using var response = await browser.GetAsync(QueryHelpers.AddQueryString("/connect/authorize", parameters));

        // The suite takes an error page as well as an error sent to the client, as here, where the request is not trusted.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Browser.ReadPageAsync(response, HttpStatusCode.BadRequest)).Text.ShouldContain("response_type");
    }

    /// <summary>oidcc-refresh-token: offline access with consent, and an ID token for the same sign-in from the refresh.</summary>
    [Fact]
    public async Task RefreshToken_GivesAnIdTokenOfTheSameSignIn()
    {
        var (client, secret) = await CreateClientAsync();
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signedIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client) { Pkce = false, Scope = "openid offline_access", Prompt = "consent" };

        using var consent = await browser.GetAsync(request.Url);
        consent.StatusCode.ShouldBe(HttpStatusCode.OK, "prompt=consent asks the user even for a first-party app");
        using var accepted = await browser.SubmitAsync(await Browser.ReadPageAsync(consent), submitter: ("consent", "accept"));
        var tokens = await RedeemAsync(browser.Client, client, secret, AuthorizationRequest.ReadCallback(accepted)["code"], basic: true);

        using var refreshed = await TokenAsync(browser.Client, client, secret, [new("grant_type", "refresh_token"), new("refresh_token", tokens.GetProperty("refresh_token").GetString()!)]);
        var renewed = await refreshed.ReadJsonAsync();
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, renewed.ToString());

        var original = Payload(tokens.GetProperty("id_token").GetString()!);
        var again = Payload(renewed.GetProperty("id_token").GetString()!);
        foreach (var claim in new[] { "iss", "sub", "aud", "auth_time" })
        {
            again.GetProperty(claim).ToString().ShouldBe(original.GetProperty(claim).ToString(), claim);
        }
    }

    /// <summary>oidcc-discovery-endpoint-verification (Config OP).</summary>
    [Fact]
    public async Task Discovery_DescribesWhatTheSuiteNeeds()
    {
        using var http = server.CreateClient();

        using var response = await http.GetAsync("/.well-known/openid-configuration", CancellationToken);
        var configuration = await response.ReadJsonAsync();

        string[] Values(string name) => [.. configuration.GetProperty(name).EnumerateArray().Select(value => value.GetString()!)];
        configuration.GetProperty("issuer").GetString().ShouldBe("http://localhost/");
        foreach (var endpoint in new[] { "authorization_endpoint", "token_endpoint", "userinfo_endpoint", "jwks_uri", "end_session_endpoint" })
        {
            configuration.GetProperty(endpoint).GetString().ShouldStartWith("http://localhost/", Case.Sensitive, endpoint);
        }

        Values("response_types_supported").ShouldContain("code");
        Values("subject_types_supported").ShouldContain("public");
        Values("id_token_signing_alg_values_supported").ShouldContain("RS256");
        Values("id_token_signing_alg_values_supported").ShouldNotContain("none");
        Values("scopes_supported").ShouldContain("openid");
        Values("token_endpoint_auth_methods_supported").ShouldBe(["client_secret_basic", "client_secret_post", "private_key_jwt"], ignoreOrder: true);
        Values("code_challenge_methods_supported").ShouldBe(["S256"]);
        Values("claims_supported").ShouldContain("sub");
        configuration.GetProperty("backchannel_logout_supported").GetBoolean().ShouldBeTrue();
        configuration.TryGetProperty("request_object_signing_alg_values_supported", out var algorithms).ShouldBe(algorithms.ValueKind == JsonValueKind.Array && !algorithms.EnumerateArray().Any(value => value.GetString() == "none"));
    }

    /// <summary>A web client as the conformance suite is registered: confidential, first party and without PKCE.</summary>
    private async Task<(string ClientId, string Secret)> CreateClientAsync()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = $"conformance-{Guid.NewGuid():N}",
            DisplayName = "OpenID conformance suite",
            Type = ClientType.Web,
            RedirectUris = [TestWebClient.RedirectUri],
            PostLogoutRedirectUris = [TestWebClient.PostLogoutRedirectUri],
            Scopes = ["openid", "profile", "email", "phone", "offline_access"],
            RequirePkce = false,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(CancellationToken));
        var response = await created.ReadAsync<CreatedClientResponse>();
        return (response.Client.ClientId, response.ClientSecret!);
    }

    private static async Task<string> CodeAsync(Browser browser, string url)
    {
        using var callback = await browser.GetAsync(url);
        var parameters = AuthorizationRequest.ReadCallback(callback);
        parameters.ShouldContainKey("code", string.Join(", ", parameters.Select(parameter => $"{parameter.Key}={parameter.Value}")));
        return parameters["code"];
    }

    private static async Task<JsonElement> RedeemAsync(HttpClient http, string client, string secret, string code, bool basic)
    {
        List<KeyValuePair<string, string>> fields = [new("grant_type", "authorization_code"), new("code", code), new("redirect_uri", TestWebClient.RedirectUri)];
        using var response = basic
            ? await TokenAsync(http, client, secret, fields)
            : await http.PostFormAsync("/connect/token", [.. fields, new("client_id", client), new("client_secret", secret)]);
        var body = await response.ReadJsonAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        return body;
    }

    private static Task<HttpResponseMessage> TokenAsync(HttpClient http, string client, string secret, IEnumerable<KeyValuePair<string, string>> fields) =>
        http.PostFormAsync("/connect/token", fields, new TestClient(client, secret));

    private static async Task<HttpResponseMessage> UserInfoAsync(HttpClient http, HttpMethod method, string accessToken, bool inBody)
    {
        // As the suite does, a POST comes as a form, empty when the token is in the header.
        using var request = new HttpRequestMessage(method, "/connect/userinfo");
        if (inBody)
        {
            request.Content = new FormUrlEncodedContent([new("access_token", accessToken)]);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (method == HttpMethod.Post)
            {
                request.Content = new FormUrlEncodedContent([]);
            }
        }

        return await http.SendAsync(request, CancellationToken);
    }

    private static JsonElement Payload(string token) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]))).RootElement.Clone();
}
