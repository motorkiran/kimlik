using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Organizations;
using Kimlik.Domain.Users;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.SocialLogin;

/// <summary>Signing in through an organization's own identity provider, over an SSO connection.</summary>
public sealed class SingleSignOnTests(KimlikServerFixture server)
{
    private const string Connections = "/api/v1/sso-connections";

    [Fact]
    public async Task NewPerson_SignsInThroughTheOrganizationsProvider_AndJoinsTheOrganization()
    {
        var provider = await TestIdentityProvider.SharedAsync(server);
        var sso = await ConnectAsync();
        var email = $"grace@{sso.Domain}";
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId);
        using var browser = new Browser(provider.Kimlik);

        using var challenge = await browser.GetAsync(request.Url);
        var signIn = await browser.GetPageAsync(challenge.Headers.Location!.OriginalString);
        using var toProvider = await browser.SubmitAsync(signIn, new Dictionary<string, string> { ["Input.Email"] = email }, action: "/signin?handler=Sso");
        using var back = await provider.SignInAsync(browser, toProvider, WorkProfile.New(email));
        using var callback = await browser.FollowAsync(back);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        provider.LoginHints.ShouldContain(email);
        Methods(tokens).ShouldBe(["fed"]);
        var user = await FindAsync(email);
        user.EmailConfirmed.ShouldBeTrue("the provider vouches for its addresses, even though registration is by invitation only");
        user.GivenName.ShouldBe("Grace");
        (await IsMemberAsync(sso.OrganizationId, user.Id)).ShouldBeTrue();
        (await LoginsAsync(user.Id)).ShouldBe([SsoConnection.LoginProviderOf(sso.Connection.Id)]);
    }

    [Fact]
    public async Task Password_LeadsToTheProvider_WhileTheConnectionIsEnabled()
    {
        var provider = await TestIdentityProvider.SharedAsync(server);
        var sso = await ConnectAsync();
        var user = await CreateUserAsync($"ada@{sso.Domain}");

        using (var browser = new Browser(provider.Kimlik))
        {
            using var signIn = await browser.SignInAsync(user.Email, user.Password);
            signIn.Headers.Location!.GetLeftPart(UriPartial.Path).ShouldBe($"{TestIdentityProvider.Issuer}/authorize");
        }

        using var api = await server.CreateApiClientAsync();
        using var disabled = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Connections}/{sso.Connection.Id}", """{ "enabled": false }""");
        disabled.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var again = new Browser(provider.Kimlik);
        using var signedIn = await again.SignInAsync(user.Email, user.Password);
        signedIn.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task ExistingAccount_IsLinked_AndTheProvidersSecondFactorCounts()
    {
        var provider = await TestIdentityProvider.SharedAsync(server);
        var sso = await ConnectAsync();
        var user = await CreateUserAsync($"ada@{sso.Domain}");
        var client = await server.CreateWebClientAsync();
        var request = new AuthorizationRequest(client.ClientId);
        using var browser = new Browser(provider.Kimlik);

        using var challenge = await browser.GetAsync(request.Url);
        var signIn = await browser.GetPageAsync(challenge.Headers.Location!.OriginalString);
        using var toProvider = await browser.SubmitAsync(signIn, new Dictionary<string, string> { ["Input.Email"] = user.Email, ["Input.Password"] = "anything" });
        var profile = WorkProfile.New(user.Email, "pwd", "mfa");
        using var back = await provider.SignInAsync(browser, toProvider, profile);
        using var callback = await browser.FollowAsync(back);
        var tokens = await OidcFlows.RedeemCodeAsync(browser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        Methods(tokens).ShouldBe(["fed", "mfa"]);
        var linked = await server.WithServicesAsync(async services =>
            (await services.GetRequiredService<UserManager<User>>().FindByLoginAsync(SsoConnection.LoginProviderOf(sso.Connection.Id), profile.Subject))?.Id);
        linked.ShouldBe(user.Id);
        (await IsMemberAsync(sso.OrganizationId, user.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task AddressOutsideTheConnectionsDomains_IsRefused()
    {
        var provider = await TestIdentityProvider.SharedAsync(server);
        var sso = await ConnectAsync();
        var outsider = $"mallory-{Guid.NewGuid():N}@example.com";
        using var browser = new Browser(provider.Kimlik);

        using var toProvider = await browser.GetAsync($"/signin/sso/{sso.Connection.Id}");
        using var back = await provider.SignInAsync(browser, toProvider, WorkProfile.New(outsider));

        (await Browser.ReadPageAsync(back)).Text.ShouldContain("did not share an email address in its domains");
        (await server.QueryDatabaseAsync(context => context.Users.AnyAsync(candidate => candidate.Email == outsider))).ShouldBeFalse();
    }

    [Fact]
    public async Task OtherFirstFactors_LeadToTheProvider_InsteadOfASession()
    {
        var provider = await TestIdentityProvider.SharedAsync(server);
        var sso = await ConnectAsync();
        var user = await CreateUserAsync($"ada@{sso.Domain}");
        var number = $"+905{Random.Shared.NextInt64(100_000_000, 999_999_999)}"[..13];
        await server.QueryDatabaseAsync(async context =>
        {
            (await context.Users.SingleAsync(candidate => candidate.Id == user.Id)).SetVerifiedPhoneNumber(number, DateTimeOffset.UtcNow);
            return await context.SaveChangesAsync();
        });
        using var browser = new Browser(provider.Kimlik);

        var page = await browser.GetPageAsync("/signin/phone");
        using var asked = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.PhoneNumber"] = number });
        var codePage = await browser.GetPageAsync(asked.Headers.Location!.OriginalString);
        using var verified = await browser.SubmitAsync(codePage, new Dictionary<string, string> { ["Input.Code"] = await server.Texts.WaitForCodeAsync(number) }, "section.card form");

        verified.Headers.Location!.OriginalString.ShouldStartWith($"/signin/sso/{sso.Connection.Id}");
        using var toProvider = await browser.GetAsync(verified.Headers.Location!.OriginalString);
        toProvider.Headers.Location!.GetLeftPart(UriPartial.Path).ShouldBe($"{TestIdentityProvider.Issuer}/authorize");
        (await browser.GetPageAsync("/")).Text.ShouldNotContain("Signed in as");
    }

    [Fact]
    public async Task Connections_TakeEveryInstallationWideSystemPermission_AndDomainsAreTheirsAlone()
    {
        var sso = await ConnectAsync();
        using var organizationManager = await server.CreateApiClientAsync(
            await server.CreateRoleWithAsync(SystemPermissions.OrganizationsRead, SystemPermissions.OrganizationsWrite));

        using var escalated = await organizationManager.Http.PostJsonAsync(Connections, Request(sso.OrganizationId, $"other-{Guid.NewGuid():N}.test"));
        escalated.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await escalated.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");
        using var listed = await organizationManager.Http.GetAsync($"{Connections}?organizationId={sso.OrganizationId}", TestContext.Current.CancellationToken);
        var connection = (await listed.ReadAsync<Page<SsoConnectionResponse>>()).Items.ShouldHaveSingleItem();
        connection.Domains.ShouldBe([sso.Domain]);
        (await listed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain(TestIdentityProvider.ClientSecret);

        using var api = await server.CreateApiClientAsync();
        using var taken = await api.Http.PostJsonAsync(Connections, Request(sso.OrganizationId, sso.Domain.ToUpperInvariant()));
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await taken.ReadProblemCodeAsync()).ShouldBe("sso.domain_taken");
        using var invalid = await api.Http.PostJsonAsync(Connections, Request(sso.OrganizationId, "localhost") with { Issuer = "http://idp.example.com" });
        (await invalid.ReadProblemCodeAsync()).ShouldBe("sso.invalid_issuer");
    }

    private static CreateSsoConnectionRequest Request(Guid organizationId, string domain) => new()
    {
        OrganizationId = organizationId,
        Name = "Acme Entra ID",
        Issuer = TestIdentityProvider.Issuer,
        ClientId = TestIdentityProvider.ClientId,
        ClientSecret = TestIdentityProvider.ClientSecret,
        Domains = [domain],
    };

    /// <summary>An organization with an enabled connection to the test provider for a domain of its own.</summary>
    private async Task<(Guid OrganizationId, string Domain, SsoConnectionResponse Connection)> ConnectAsync()
    {
        var organization = await server.CreateOrganizationAsync();
        var domain = $"acme-{Guid.NewGuid():N}.test";
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync(Connections, Request(organization.Id, domain));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (organization.Id, domain, await created.ReadAsync<SsoConnectionResponse>());
    }

    private Task<TestUser> CreateUserAsync(string email) =>
        server.WithServicesAsync(async services =>
        {
            var user = User.Create(email, "Ada", "Lovelace", "en", DateTimeOffset.UtcNow);
            user.EmailConfirmed = true;
            user.MarkPasskeyOffered(DateTimeOffset.UtcNow);
            (await services.GetRequiredService<UserManager<User>>().CreateAsync(user, TestUsers.Password)).Succeeded.ShouldBeTrue();
            return new TestUser(user.Id, email, TestUsers.Password);
        });

    private Task<User> FindAsync(string email) =>
        server.WithServicesAsync(async services => (await services.GetRequiredService<UserManager<User>>().FindByEmailAsync(email)).ShouldNotBeNull());

    private Task<bool> IsMemberAsync(Guid organizationId, Guid userId) =>
        server.QueryDatabaseAsync(context => context.Memberships.AnyAsync(membership => membership.OrganizationId == organizationId && membership.UserId == userId));

    private Task<List<string>> LoginsAsync(Guid userId) =>
        server.QueryDatabaseAsync(context => context.Set<IdentityUserLogin<Guid>>().Where(login => login.UserId == userId).Select(login => login.LoginProvider).ToListAsync());

    private static string[] Methods(JsonElement tokens)
    {
        var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(tokens.GetProperty("access_token").GetString()!.Split('.')[1]))).RootElement;
        return [.. payload.GetProperty("amr").EnumerateArray().Select(value => value.GetString()!)];
    }
}
