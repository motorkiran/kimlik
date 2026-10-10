using System.Text;
using System.Text.Json;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Mfa;
using Kimlik.Server.Tests.Passkeys;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>Apps ask how strongly users signed in (<c>acr_values</c>), and tokens say it (<c>acr</c>).</summary>
public sealed class AuthenticationContextTests(KimlikServerFixture server)
{
    private const string MultiFactor = "http://schemas.openid.net/pape/policies/2007/06/multi-factor";
    private const string PhishingResistant = "http://schemas.openid.net/pape/policies/2007/06/phishing-resistant";

    [Fact]
    public async Task MultiFactorRequest_SendsAPasswordSessionToTheSecondStep_AndTheTokenSaysSo()
    {
        var user = await server.CreateUserAsync();
        var factor = await server.EnableMfaAsync(user.Id);
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var challenge = await browser.GetPageAsync(signIn.Headers.Location!.OriginalString);
        using var verified = await browser.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = (await TotpCodes.NextThreeAsync(factor.Secret))[1] });
        var request = new AuthorizationRequest(client.ClientId) { AcrValues = MultiFactor };

        using var callback = await browser.GetAsync(request.Url);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Payload(tokens, "id_token").GetProperty("acr").GetString().ShouldBe(MultiFactor);
        Payload(tokens, "access_token").GetProperty("acr").GetString().ShouldBe(MultiFactor);
    }

    [Fact]
    public async Task MultiFactorRequest_OfAnAccountWithoutASecondFactor_SetsOneUp()
    {
        var user = await server.CreateUserAsync();
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        using var stepUp = await browser.GetAsync(new AuthorizationRequest(client.ClientId) { AcrValues = MultiFactor }.Url);

        stepUp.Headers.Location!.OriginalString.ShouldStartWith("/signin/set-up-two-factor?");
    }

    [Fact]
    public async Task PhishingResistantRequest_AsksForAPasskey_EvenAfterACode()
    {
        var user = await server.CreateUserAsync();
        using var authenticator = new SoftwareAuthenticator();
        using (var setUp = new Browser(server))
        {
            using var signInToAdd = await setUp.SignInAsync(user.Email, user.Password);
            await setUp.AddPasskeyAsync(authenticator);
        }

        var factor = await server.EnableMfaAsync(user.Id);
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var challenge = await browser.GetPageAsync(signIn.Headers.Location!.OriginalString);
        challenge.Document.QuerySelector("#passkey-verify").ShouldNotBeNull();
        using var verified = await browser.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = (await TotpCodes.NextThreeAsync(factor.Secret))[1] });
        var request = new AuthorizationRequest(client.ClientId) { AcrValues = PhishingResistant };

        using var stepUp = await browser.GetAsync(request.Url);
        stepUp.Headers.Location!.OriginalString.ShouldStartWith("/signin/two-factor?");
        var page = await browser.GetPageAsync(stepUp.Headers.Location.OriginalString);
        page.Document.QuerySelector("input[name='Input.Code']").ShouldBeNull();
        using var withPasskey = await browser.RunPasskeyFormAsync(page, "form", options => authenticator.Get(options), buttonSelector: "#passkey-verify");
        using var callback = await browser.FollowAsync(withPasskey);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Payload(tokens, "id_token").GetProperty("acr").GetString().ShouldBe(PhishingResistant);
    }

    [Fact]
    public async Task PasswordOnlySignIn_HasNoAcr()
    {
        var user = await server.CreateUserAsync();

        var (browser, _, tokens) = await server.SignInAndRedeemAsync(user);
        browser.Dispose();

        Payload(tokens, "id_token").TryGetProperty("acr", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Discovery_ListsThePolicies()
    {
        using var http = server.CreateClient();

        using var response = await http.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        var configuration = await response.ReadJsonAsync();

        configuration.GetProperty("acr_values_supported").EnumerateArray().Select(value => value.GetString()).ShouldBe([PhishingResistant, MultiFactor]);
    }

    private static JsonElement Payload(JsonElement tokens, string token) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty(token).GetString()!.Split('.')[1]))).RootElement.Clone();
}
