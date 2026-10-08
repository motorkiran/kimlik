using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Organizations;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Mfa;

/// <summary>Sessions that started with a password alone gain a second factor when signing in to an organization that requires one.</summary>
public sealed class MfaStepUpTests(KimlikServerFixture server)
{
    [Fact]
    public async Task MemberWithoutASecondFactor_SetsOneUp_ToSignInToTheOrganization()
    {
        var user = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(requireMfa: true, user.Id);
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var request = new AuthorizationRequest(client.ClientId) { Organization = organization.Slug };

        using var stepUp = await browser.GetAsync(request.Url);
        stepUp.Headers.Location!.OriginalString.ShouldStartWith("/signin/set-up-two-factor");
        var setup = await browser.GetPageAsync(stepUp.Headers.Location!.OriginalString);
        var secret = setup.Document.GetElementById("secret")!.TextContent.Replace(" ", string.Empty, StringComparison.Ordinal);
        var codes = await TotpCodes.NextThreeAsync(secret);
        var done = await Browser.ReadPageAsync(await browser.SubmitAsync(setup, new Dictionary<string, string> { ["Code"] = codes[1] }));

        var continueUrl = done.Document.QuerySelector("a.button")!.GetAttribute("href")!;
        using var callback = await browser.FollowAsync(await browser.GetAsync(continueUrl));
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        var payload = Payload(tokens);
        payload.GetProperty(KimlikClaimTypes.OrganizationId).GetString().ShouldBe(organization.Id.ToString());
        payload.GetProperty("amr").EnumerateArray().Select(value => value.GetString()).ShouldContain("mfa");
    }

    [Fact]
    public async Task PasswordSession_OfAUserWithASecondFactor_VerifiesACode()
    {
        var user = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(requireMfa: true, user.Id);
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        // Set up after the session started, so the session itself rests on the password alone.
        var factor = await server.EnableMfaAsync(user.Id);
        var request = new AuthorizationRequest(client.ClientId) { Organization = organization.Slug };

        using var stepUp = await browser.GetAsync(request.Url);
        var challenge = await browser.GetPageAsync(stepUp.Headers.Location!.OriginalString);
        var codes = await TotpCodes.NextThreeAsync(factor.Secret);
        using var verified = await browser.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = codes[1] });
        using var callback = await browser.FollowAsync(await browser.GetAsync(verified.Headers.Location!.OriginalString));

        AuthorizationRequest.ReadCallback(callback).ShouldContainKey("code");
    }

    [Fact]
    public async Task SilentRequest_CannotStepUp()
    {
        var user = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(requireMfa: true, user.Id);
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        using var callback = await browser.GetAsync(new AuthorizationRequest(client.ClientId) { Organization = organization.Slug, Prompt = "none" }.Url);

        AuthorizationRequest.ReadCallback(callback)["error"].ShouldBe("interaction_required");
    }

    [Fact]
    public async Task OrganizationRequirement_IsSetThroughTheApi()
    {
        using var api = await server.CreateApiClientAsync();
        var organization = await server.CreateOrganizationAsync();

        using var updated = await api.Http.SendJsonAsync(HttpMethod.Patch, $"/api/v1/organizations/{organization.Id}", """{ "requireMfa": true }""");

        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await updated.ReadAsync<OrganizationResponse>()).RequireMfa.ShouldBeTrue();
    }

    private static JsonElement Payload(JsonElement tokens) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty("access_token").GetString()!.Split('.')[1]))).RootElement.Clone();
}
