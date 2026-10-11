using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Mfa;
using Kimlik.Server.Tests.Oidc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Interactions;

/// <summary>
/// Sign-ins handed to a login app of the developer's own (design §10.12). The tests play the login app: they read the
/// challenge Kimlik sends the browser with, and answer it through the Management API.
/// </summary>
public sealed class LoginAppTests(KimlikServerFixture server)
{
    private const string LoginUrl = "https://login.example.test/sign-in";

    private static readonly SemaphoreSlim Starting = new(1, 1);
    private static WebApplicationFactory<Program>? _kimlik;

    [Fact]
    public async Task LoginApp_SignsAPersonIn_WithTheirKimlikPasswordAndSecondFactor()
    {
        var kimlik = await KimlikAsync();
        var user = await server.CreateUserAsync();
        var secondFactor = await server.EnableMfaAsync(user.Id);
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId);
        using var loginApp = await LoginAppAsync();
        using var browser = new Browser(kimlik);

        using var handedOver = await browser.GetAsync(request.Url);
        var challenge = ChallengeOf(handedOver);
        var asked = await loginApp.Http.GetAsync($"/api/v1/interactions/login/{challenge}", TestContext.Current.CancellationToken);
        (await asked.ReadAsync<LoginChallengeResponse>()).ClientId.ShouldBe(client.ClientId);

        using var password = await loginApp.Http.PostJsonAsync(
            $"/api/v1/interactions/login/{challenge}/password", new VerifyPasswordRequest { Email = user.Email, Password = user.Password });
        var verified = await password.ReadAsync<VerifiedUserResponse>();
        verified.UserId.ShouldBe(user.Id);
        verified.SecondFactorRequired.ShouldBeTrue();
        using var tooWeak = await AcceptAsync(loginApp, challenge, user.Id, "pwd");
        (await tooWeak.ReadProblemCodeAsync()).ShouldBe("interaction.factors_missing");
        using var code = await loginApp.Http.PostJsonAsync(
            $"/api/v1/interactions/login/{challenge}/second-factor", new VerifySecondFactorRequest { UserId = user.Id, Code = (await TotpCodes.NextThreeAsync(secondFactor.Secret))[1] });
        code.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var accepted = await AcceptAsync(loginApp, challenge, user.Id, "pwd", "otp");

        using var back = await browser.GetAsync((await accepted.ReadAsync<LoginChallengeRedirect>()).RedirectTo);
        using var callback = await browser.FollowAsync(back);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);
        Methods(tokens).ShouldBe(["pwd", "otp", "mfa"]);
    }

    [Fact]
    public async Task RejectedChallenge_ReachesTheApp_AsAccessDenied()
    {
        var kimlik = await KimlikAsync();
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId);
        using var loginApp = await LoginAppAsync();
        using var browser = new Browser(kimlik);
        using var handedOver = await browser.GetAsync(request.Url);

        using var rejected = await loginApp.Http.PostJsonAsync(
            $"/api/v1/interactions/login/{ChallengeOf(handedOver)}/reject", new RejectLoginChallengeRequest { Reason = "Not today." });
        using var back = await browser.GetAsync((await rejected.ReadAsync<LoginChallengeRedirect>()).RedirectTo);
        using var callback = await browser.FollowAsync(back);

        var answer = AuthorizationRequest.ReadCallback(callback);
        answer["error"].ShouldBe("access_denied");
        answer["error_description"].ShouldBe("Not today.");
    }

    [Fact]
    public async Task OnlyTheBrowserThatWasSent_CompletesTheChallenge_FromAnyOfItsTabs_AndOnlyOnce()
    {
        var kimlik = await KimlikAsync();
        var user = await server.CreateUserAsync();
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId);
        using var loginApp = await LoginAppAsync();
        using var browser = new Browser(kimlik);
        using var handedOver = await browser.GetAsync(request.Url);
        using var otherTab = await browser.GetAsync(new AuthorizationRequest(client.ClientId).Url);
        using var accepted = await AcceptAsync(loginApp, ChallengeOf(handedOver), user.Id, "pwd");
        var redirectTo = (await accepted.ReadAsync<LoginChallengeRedirect>()).RedirectTo;

        using var stranger = new Browser(kimlik);
        (await Browser.ReadPageAsync(await stranger.GetAsync(redirectTo))).Text.ShouldContain("This sign-in can no longer be completed.");

        using var back = await browser.GetAsync(redirectTo);
        back.Headers.Location!.OriginalString.ShouldStartWith("/connect/authorize");
        (await Browser.ReadPageAsync(await browser.GetAsync(redirectTo))).Text.ShouldContain("This sign-in can no longer be completed.");
    }

    [Fact]
    public async Task LoginApp_CannotSignInAdministrators_WhoUseTheHostedPages()
    {
        var kimlik = await KimlikAsync();
        var administrator = await server.CreateUserAsync();
        await server.AssignToUserAsync(administrator.Id, SystemRoles.Admin);
        var client = await server.CreateWebClientAsync();
        using var loginApp = await LoginAppAsync();
        using var browser = new Browser(kimlik);
        using var handedOver = await browser.GetAsync(new AuthorizationRequest(client.ClientId).Url);
        var challenge = ChallengeOf(handedOver);

        using var refused = await AcceptAsync(loginApp, challenge, administrator.Id, "pwd", "otp");

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");
        using var asked = await loginApp.Http.GetAsync($"/api/v1/interactions/login/{challenge}", TestContext.Current.CancellationToken);
        (await asked.ReadAsync<LoginChallengeResponse>()).HostedSignInUrl.ShouldStartWith("http://localhost/signin?ReturnUrl=");
    }

    [Fact]
    public async Task CredentialChecks_AreLimitedPerChallenge()
    {
        var kimlik = await KimlikAsync();
        var client = await server.CreateWebClientAsync();
        using var loginApp = await LoginAppAsync();
        using var browser = new Browser(kimlik);
        using var handedOver = await browser.GetAsync(new AuthorizationRequest(client.ClientId).Url);
        var challenge = ChallengeOf(handedOver);
        var guess = new VerifyPasswordRequest { Email = $"nobody-{Guid.NewGuid():N}@example.com", Password = "a guess at a password" };

        // Sent at once, as a login app could to get past the limit.
        var answers = await Task.WhenAll(Enumerable.Range(0, 14).Select(async _ =>
        {
            using var response = await loginApp.Http.PostJsonAsync($"/api/v1/interactions/login/{challenge}/password", guess);
            return await response.ReadProblemCodeAsync();
        }));

        answers.Count(code => code == "interaction.invalid_credentials").ShouldBe(10);
        answers.Count(code => code == "interaction.too_many_attempts").ShouldBe(4);
    }

    [Fact]
    public async Task RightPassword_DoesNotForgetWrongSecondFactorCodes()
    {
        var kimlik = await KimlikAsync();
        var user = await server.CreateUserAsync();
        await server.EnableMfaAsync(user.Id);
        var client = await server.CreateWebClientAsync();
        using var loginApp = await LoginAppAsync();
        using var browser = new Browser(kimlik);
        using var handedOver = await browser.GetAsync(new AuthorizationRequest(client.ClientId).Url);
        var challenge = ChallengeOf(handedOver);

        // Five wrong codes lock the account, with a right password in between.
        foreach (var wrongCodes in new[] { 4, 1 })
        {
            using var password = await VerifyPasswordAsync(loginApp, challenge, user);
            password.StatusCode.ShouldBe(HttpStatusCode.OK);
            for (var attempt = 0; attempt < wrongCodes; attempt++)
            {
                using var wrong = await loginApp.Http.PostJsonAsync(
                    $"/api/v1/interactions/login/{challenge}/second-factor", new VerifySecondFactorRequest { UserId = user.Id, Code = "000000" });
                (await wrong.ReadProblemCodeAsync()).ShouldBe("interaction.wrong_code");
            }
        }

        using var locked = await VerifyPasswordAsync(loginApp, challenge, user);
        (await locked.ReadProblemCodeAsync()).ShouldBe("interaction.invalid_credentials");
    }

    private static Task<HttpResponseMessage> VerifyPasswordAsync(ApiClient loginApp, string challenge, TestUser user) =>
        loginApp.Http.PostJsonAsync($"/api/v1/interactions/login/{challenge}/password", new VerifyPasswordRequest { Email = user.Email, Password = user.Password });

    /// <summary>A Kimlik that hands sign-ins to the login app; it shares the database, and only its configuration differs.</summary>
    private async Task<WebApplicationFactory<Program>> KimlikAsync()
    {
        await Starting.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (_kimlik is null)
            {
                var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kimlik:Interactions:LoginUrl"] = LoginUrl })));
                await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
                _kimlik = kimlik;
            }

            return _kimlik;
        }
        finally
        {
            Starting.Release();
        }
    }

    private async Task<ApiClient> LoginAppAsync() => await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.InteractionsWrite));

    private static string ChallengeOf(HttpResponseMessage handedOver)
    {
        var location = handedOver.Headers.Location ?? throw new InvalidOperationException($"Expected a redirect to the login app, got {(int)handedOver.StatusCode}.");
        location.GetLeftPart(UriPartial.Path).ShouldBe(LoginUrl);
        return QueryHelpers.ParseQuery(location.Query)["login_challenge"].ToString();
    }

    private static Task<HttpResponseMessage> AcceptAsync(ApiClient loginApp, string challenge, Guid userId, params string[] methods) =>
        loginApp.Http.PostJsonAsync($"/api/v1/interactions/login/{challenge}/accept", new AcceptLoginChallengeRequest { UserId = userId, Methods = methods });

    private static string[] Methods(JsonElement tokens)
    {
        var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty("access_token").GetString()!.Split('.')[1]))).RootElement;
        return [.. payload.GetProperty("amr").EnumerateArray().Select(value => value.GetString()!)];
    }
}
