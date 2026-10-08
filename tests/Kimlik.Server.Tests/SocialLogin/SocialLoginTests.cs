using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Application.Accounts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Users;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Mfa;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.SocialLogin;

/// <summary>Signing up and in with an account at another provider, through the hosted pages.</summary>
public sealed class SocialLoginTests(KimlikServerFixture server)
{
    private const string ProviderButton = "form[action^='/signin/external']";

    [Fact]
    public async Task NewPerson_SignsUpWithTheProvider_AndTokensSaySo()
    {
        var provider = await server.TestProviderAsync();
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(provider.Kimlik);
        var request = new AuthorizationRequest(client.ClientId);
        var profile = TestProfile.New();

        using var challenge = await browser.GetAsync(request.Url);
        var signIn = await browser.GetPageAsync(challenge.Headers.Location!.OriginalString);
        signIn.Text.ShouldContain($"Continue with {TestProvider.DisplayName}");

        using var toProvider = await browser.SubmitAsync(signIn, formSelector: ProviderButton);
        using var back = await provider.SignInAsync(browser, toProvider, profile);
        using var callback = await browser.FollowAsync(back);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Methods(tokens).ShouldBe(["fed"]);
        var user = await FindAsync(profile.Email!);
        user.EmailConfirmed.ShouldBeTrue();
        user.GivenName.ShouldBe("Grace");
        (await LinkedUserAsync(profile)).ShouldBe(user.Id);
    }

    [Fact]
    public async Task LinkedAccount_SignsInToTheSameUser()
    {
        var provider = await server.TestProviderAsync();
        var profile = TestProfile.New();

        using (var first = new Browser(provider.Kimlik))
        {
            (await SignInAsync(provider, first, profile)).Headers.Location!.OriginalString.ShouldBe("/");
        }

        using var second = new Browser(provider.Kimlik);
        using var signedIn = await SignInAsync(provider, second, profile);

        signedIn.Headers.Location!.OriginalString.ShouldBe("/");
        (await second.GetPageAsync("/")).Text.ShouldContain($"Signed in as {profile.Email}.");
    }

    [Fact]
    public async Task UnverifiedAddress_IsVerifiedByEmailFirst()
    {
        var provider = await server.TestProviderAsync();
        var profile = TestProfile.New(emailVerified: false);
        using var browser = new Browser(provider.Kimlik);

        using var signedUp = await SignInAsync(provider, browser, profile);

        signedUp.Headers.Location!.OriginalString.ShouldBe("/signup/check-email");
        (await FindAsync(profile.Email!)).EmailConfirmed.ShouldBeFalse();
        await server.Emails.WaitForAsync(profile.Email!, "Confirm your email address");

        using var again = new Browser(provider.Kimlik);
        using var refused = await SignInAsync(provider, again, profile);
        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("Confirm your email address before signing in.");
    }

    [Fact]
    public async Task AddressOfAnExistingAccount_IsLinkedOnlyOnceItsOwnerSignsIn()
    {
        var provider = await server.TestProviderAsync();
        var owner = await server.CreateUserAsync();
        var profile = TestProfile.New(owner.Email);
        using var browser = new Browser(provider.Kimlik);

        using var offered = await SignInAsync(provider, browser, profile);
        offered.Headers.Location!.OriginalString.ShouldStartWith("/signin/link");
        var link = await browser.GetPageAsync(offered.Headers.Location!.OriginalString);
        link.Text.ShouldContain($"An account with {owner.Email} already exists.");
        (await LinkedUserAsync(profile)).ShouldBeNull();

        var signIn = await browser.GetPageAsync(link.Document.QuerySelector("a.button")!.GetAttribute("href")!);
        using var signedIn = await browser.SubmitAsync(signIn, new Dictionary<string, string> { ["Input.Email"] = owner.Email, ["Input.Password"] = owner.Password });
        var confirm = await browser.GetPageAsync(signedIn.Headers.Location!.OriginalString);
        confirm.Text.ShouldContain($"Connect your {TestProvider.DisplayName} account to {owner.Email}?");

        using var connected = await browser.SubmitAsync(confirm, formSelector: "#connect");
        connected.Headers.Location!.OriginalString.ShouldBe("/");
        (await LinkedUserAsync(profile)).ShouldBe(owner.Id);

        using var later = new Browser(provider.Kimlik);
        (await SignInAsync(provider, later, profile)).Headers.Location!.OriginalString.ShouldBe("/");
        (await later.GetPageAsync("/")).Text.ShouldContain($"Signed in as {owner.Email}.");
    }

    [Fact]
    public async Task SecondFactor_FollowsTheProvider()
    {
        var provider = await server.TestProviderAsync();
        var profile = TestProfile.New();
        using (var signUp = new Browser(provider.Kimlik))
        {
            using var _ = await SignInAsync(provider, signUp, profile);
        }

        var factor = await server.EnableMfaAsync((await FindAsync(profile.Email!)).Id);
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(provider.Kimlik);
        var request = new AuthorizationRequest(client.ClientId);

        using var challenge = await browser.GetAsync(request.Url);
        var signIn = await browser.GetPageAsync(challenge.Headers.Location!.OriginalString);
        using var toProvider = await browser.SubmitAsync(signIn, formSelector: ProviderButton);
        using var back = await provider.SignInAsync(browser, toProvider, profile);
        back.Headers.Location!.OriginalString.ShouldStartWith("/signin/two-factor");

        var codes = await TotpCodes.NextThreeAsync(factor.Secret);
        var twoFactor = await browser.GetPageAsync(back.Headers.Location!.OriginalString);
        using var verified = await browser.SubmitAsync(twoFactor, new Dictionary<string, string> { ["Input.Code"] = codes[1] });
        using var callback = await browser.FollowAsync(await browser.GetAsync(verified.Headers.Location!.OriginalString));
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Methods(tokens).ShouldBe(["fed", "otp", "mfa"]);
    }

    [Fact]
    public async Task PasswordReset_UnlinksAccountsLinkedBeforeTheAddressWasProven()
    {
        await using var provider = await TestProvider.StartAsync(server, trustEmail: false);
        var profile = TestProfile.New();
        using (var squatter = new Browser(provider.Kimlik))
        {
            using var _ = await SignInAsync(provider, squatter, profile);
        }

        var user = await FindAsync(profile.Email!);
        (await LinkedUserAsync(profile)).ShouldBe(user.Id);

        // The owner of the address claims the account by resetting its password.
        var reset = await server.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<User>>();
            var token = await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync(user.Id.ToString()))!);
            return await services.GetRequiredService<ResetPasswordHandler>().HandleAsync(
                new ResetPasswordCommand(user.Id, token, "the owner's own password"), TestContext.Current.CancellationToken);
        });

        reset.IsSuccess.ShouldBeTrue();
        (await LinkedUserAsync(profile)).ShouldBeNull();
        (await FindAsync(profile.Email!)).EmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task ClosedRegistration_TurnsNewPeopleAway()
    {
        await using var provider = await TestProvider.StartAsync(server, configuration: new Dictionary<string, string?> { ["Kimlik:Accounts:Registration"] = "InviteOnly" });
        var profile = TestProfile.New();
        using var browser = new Browser(provider.Kimlik);

        using var refused = await SignInAsync(provider, browser, profile);

        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("Registration is closed.");
        (await LinkedUserAsync(profile)).ShouldBeNull();
    }

    [Fact]
    public async Task InviteOnlyRegistration_LetsInvitedPeopleIn_WithAnAddressTheProviderVerified()
    {
        await using var provider = await TestProvider.StartAsync(server, configuration: new Dictionary<string, string?> { ["Kimlik:Accounts:Registration"] = "InviteOnly" });
        var invitee = TestProfile.New();
        var unverified = TestProfile.New(emailVerified: false);
        using var api = await server.CreateApiClientAsync();
        var organization = await server.CreateOrganizationAsync();
        foreach (var profile in new[] { invitee, unverified })
        {
            using var invited = await api.Http.PostJsonAsync(
                $"/api/v1/organizations/{organization.Id}/invitations", new CreateInvitationRequest { Email = profile.Email! });
        }

        using (var browser = new Browser(provider.Kimlik))
        {
            using var signedUp = await SignInAsync(provider, browser, invitee);
        }

        using (var browser = new Browser(provider.Kimlik))
        {
            using var refused = await SignInAsync(provider, browser, unverified);
            (await Browser.ReadPageAsync(refused)).Text.ShouldContain("Registration is closed.");
        }

        (await LinkedUserAsync(invitee)).ShouldNotBeNull();
        (await LinkedUserAsync(unverified)).ShouldBeNull();
    }

    [Fact]
    public async Task UntrustedProvider_LeavesTheAddressToBeVerified()
    {
        await using var provider = await TestProvider.StartAsync(server, trustEmail: false);
        var profile = TestProfile.New(emailVerified: true);
        using var browser = new Browser(provider.Kimlik);

        using var signedUp = await SignInAsync(provider, browser, profile);

        signedUp.Headers.Location!.OriginalString.ShouldBe("/signup/check-email");
        (await FindAsync(profile.Email!)).EmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task ProviderWithoutAnAddress_CannotCreateAnAccount()
    {
        var provider = await server.TestProviderAsync();
        using var browser = new Browser(provider.Kimlik);

        using var refused = await SignInAsync(provider, browser, TestProfile.New() with { Email = null });

        (await Browser.ReadPageAsync(refused)).Text.ShouldContain($"Your {TestProvider.DisplayName} account does not share an email address");
    }

    [Fact]
    public async Task DeclinedOrForgedResponses_DoNotSignIn()
    {
        var provider = await server.TestProviderAsync();
        using var browser = new Browser(provider.Kimlik);
        var signIn = await browser.GetPageAsync("/signin");
        using var toProvider = await browser.SubmitAsync(signIn, formSelector: ProviderButton);
        var query = QueryHelpers.ParseQuery(toProvider.Headers.Location!.Query);

        using var declined = await browser.GetAsync(QueryHelpers.AddQueryString(query["redirect_uri"].ToString(), new Dictionary<string, string?>
        {
            ["error"] = "access_denied",
            ["state"] = query["state"].ToString(),
        }));
        (await Browser.ReadPageAsync(declined, HttpStatusCode.BadRequest)).Text.ShouldContain("Signing in with that account did not work.");

        // A response from another browser's sign-in, without the cookie that binds it to this one.
        using var elsewhere = new Browser(provider.Kimlik);
        using var forged = await provider.SignInAsync(elsewhere, toProvider, TestProfile.New());
        (await Browser.ReadPageAsync(forged, HttpStatusCode.BadRequest)).Text.ShouldContain("Signing in with that account did not work.");
    }

    /// <summary>Signs in at the provider from the sign-in page, and returns Kimlik's answer when the browser comes back.</summary>
    private static async Task<HttpResponseMessage> SignInAsync(TestProvider provider, Browser browser, TestProfile profile)
    {
        var signIn = await browser.GetPageAsync("/signin");
        using var toProvider = await browser.SubmitAsync(signIn, formSelector: ProviderButton);
        return await provider.SignInAsync(browser, toProvider, profile);
    }

    private Task<User> FindAsync(string email) =>
        server.WithServicesAsync(async services => (await services.GetRequiredService<UserManager<User>>().FindByEmailAsync(email)).ShouldNotBeNull());

    private Task<Guid?> LinkedUserAsync(TestProfile profile) =>
        server.WithServicesAsync(async services =>
            (await services.GetRequiredService<UserManager<User>>().FindByLoginAsync(TestProvider.Name, profile.Subject))?.Id);

    private static string[] Methods(JsonElement tokens)
    {
        var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty("access_token").GetString()!.Split('.')[1]))).RootElement;
        return [.. payload.GetProperty("amr").EnumerateArray().Select(value => value.GetString()!)];
    }
}
