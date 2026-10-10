using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.SocialLogin;

/// <summary>Signing in through an organization's SAML provider, and the responses Kimlik refuses.</summary>
public sealed class SamlTests(KimlikServerFixture server) : IDisposable
{
    private readonly TestSamlProvider _provider = new();

    [Fact]
    public async Task NewPerson_SignsInThroughTheSamlProvider_AndJoinsTheOrganization()
    {
        var (organizationId, domain, _) = await ConnectAsync();
        var email = $"grace@{domain}";
        using var browser = new Browser(server);

        var signIn = await browser.GetPageAsync("/signin");
        using var toProvider = await browser.SubmitAsync(signIn, new Dictionary<string, string> { ["Input.Email"] = email }, action: "/signin?handler=Sso");
        var request = TestSamlProvider.ReadRequest(toProvider);
        request.Issuer.ShouldBe("http://localhost/signin/sso/saml");
        request.AssertionConsumerService.ShouldBe("http://localhost/signin/sso/saml/acs");
        var response = _provider.Respond(request, $"employee-{Guid.NewGuid():N}", email);
        using var signedIn = await TestSamlProvider.PostAsync(browser, request, response);

        signedIn.Headers.Location!.OriginalString.ShouldBe("/");
        (await browser.GetPageAsync("/")).Text.ShouldContain($"Signed in as {email}.");
        using var replayed = await TestSamlProvider.PostAsync(browser, request, response);
        (await Browser.ReadPageAsync(replayed)).Text.ShouldContain("Signing in with your organization did not work.");
        var user = await server.QueryDatabaseAsync(context => context.Users.SingleAsync(candidate => candidate.Email == email));
        user.EmailConfirmed.ShouldBeTrue();
        user.GivenName.ShouldBe("Grace");
        (await server.QueryDatabaseAsync(context => context.Memberships.AnyAsync(membership => membership.OrganizationId == organizationId && membership.UserId == user.Id)))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task LinkedPerson_SignsInAgain_ByTheirNameId()
    {
        var (_, domain, connection) = await ConnectAsync();
        var email = $"grace@{domain}";
        var subject = $"employee-{Guid.NewGuid():N}";
        using (var first = new Browser(server))
        {
            using var toProvider = await first.GetAsync($"/signin/sso/{connection.Id}");
            var request = TestSamlProvider.ReadRequest(toProvider);
            using var _ = await TestSamlProvider.PostAsync(first, request, _provider.Respond(request, subject, email));
        }

        using var again = new Browser(server);
        using var toProviderAgain = await again.GetAsync($"/signin/sso/{connection.Id}");
        var requestAgain = TestSamlProvider.ReadRequest(toProviderAgain);
        using var signedIn = await TestSamlProvider.PostAsync(again, requestAgain, _provider.Respond(requestAgain, subject, email: null));

        signedIn.Headers.Location!.OriginalString.ShouldBe("/");
    }

    public static TheoryData<string> Forgeries => ["another certificate", "unsigned", "another request", "another audience"];

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task ForgedOrMisdirectedResponses_AreRefused(string forgery)
    {
        var (_, domain, connection) = await ConnectAsync();
        var email = $"mallory@{domain}";
        using var otherKey = RSA.Create(2048);
        using var otherCertificate = new CertificateRequest("CN=saml.test", otherKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        using var browser = new Browser(server);
        using var toProvider = await browser.GetAsync($"/signin/sso/{connection.Id}");
        var request = TestSamlProvider.ReadRequest(toProvider);
        var subject = $"employee-{Guid.NewGuid():N}";

        var response = forgery switch
        {
            "another certificate" => _provider.Respond(request, subject, email, signedWith: otherCertificate),
            "unsigned" => _provider.Respond(request, subject, email, signed: false),
            "another request" => _provider.Respond(request, subject, email, inResponseTo: $"_{Guid.NewGuid():N}"),
            _ => _provider.Respond(request, subject, email, audience: "https://elsewhere.test/saml"),
        };
        using var refused = await TestSamlProvider.PostAsync(browser, request, response);

        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("Signing in with your organization did not work.");
        (await server.QueryDatabaseAsync(context => context.Users.AnyAsync(user => user.Email == email))).ShouldBeFalse();
    }

    [Fact]
    public async Task ResponseTheBrowserDidNotAskFor_IsRefused()
    {
        var (_, domain, connection) = await ConnectAsync();
        var email = $"mallory@{domain}";
        using var asking = new Browser(server);
        using var toProvider = await asking.GetAsync($"/signin/sso/{connection.Id}");
        var request = TestSamlProvider.ReadRequest(toProvider);
        var response = _provider.Respond(request, $"employee-{Guid.NewGuid():N}", email);

        // The response goes to a browser that never started a sign-in, as in login CSRF or a provider-initiated sign-in.
        using var other = new Browser(server);
        using var refused = await TestSamlProvider.PostAsync(other, request, response);

        (await Browser.ReadPageAsync(refused)).Text.ShouldContain("Signing in with your organization did not work.");
        (await server.QueryDatabaseAsync(context => context.Users.AnyAsync(user => user.Email == email))).ShouldBeFalse();
    }

    [Fact]
    public async Task Metadata_DescribesKimlikAsAServiceProvider()
    {
        using var http = server.CreateClient();

        using var response = await http.GetAsync("/signin/sso/saml/metadata", TestContext.Current.CancellationToken);

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/samlmetadata+xml");
        var metadata = XDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Root!;
        metadata.Attribute("entityID")!.Value.ShouldBe("http://localhost/signin/sso/saml");
        metadata.Descendants().Single(element => element.Name.LocalName == "AssertionConsumerService").Attribute("Location")!.Value
            .ShouldBe("http://localhost/signin/sso/saml/acs");
    }

    [Fact]
    public async Task SamlConnections_RequireTheProvidersCertificate()
    {
        var organization = await server.CreateOrganizationAsync();
        using var api = await server.CreateApiClientAsync();

        using var created = await api.Http.PostJsonAsync("/api/v1/sso-connections", Request(organization.Id, $"acme-{Guid.NewGuid():N}.test") with { Certificate = "not a certificate" });

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await created.ReadProblemCodeAsync()).ShouldBe("sso.invalid_certificate");
    }

    public void Dispose() => _provider.Dispose();

    private CreateSsoConnectionRequest Request(Guid organizationId, string domain) => new()
    {
        OrganizationId = organizationId,
        Name = "Acme AD FS",
        Protocol = SsoProtocol.Saml,
        Issuer = TestSamlProvider.EntityId,
        SignOnUrl = TestSamlProvider.SignOnUrl,
        Certificate = _provider.Certificate.ExportCertificatePem(),
        Domains = [domain],
    };

    /// <summary>An organization with an enabled SAML connection to the test provider for a domain of its own.</summary>
    private async Task<(Guid OrganizationId, string Domain, SsoConnectionResponse Connection)> ConnectAsync()
    {
        var organization = await server.CreateOrganizationAsync();
        var domain = $"acme-{Guid.NewGuid():N}.test";
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync("/api/v1/sso-connections", Request(organization.Id, domain));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (organization.Id, domain, await created.ReadAsync<SsoConnectionResponse>());
    }
}
