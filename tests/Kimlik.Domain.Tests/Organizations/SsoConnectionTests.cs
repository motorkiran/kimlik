using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kimlik.Domain.Organizations;

namespace Kimlik.Domain.Tests.Organizations;

public sealed class SsoConnectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    private static SsoConnectionSettings Oidc(IEnumerable<string> domains) => new("Acme", "https://acme.okta.com", domains, Enabled: true) { ClientId = "kimlik" };

    [Theory]
    [InlineData("Acme.COM.", "acme.com")]
    [InlineData(" eu.acme.com ", "eu.acme.com")]
    [InlineData("acme", null)]
    [InlineData("acme .com", null)]
    [InlineData("@acme.com", null)]
    [InlineData("", null)]
    public void Domains_AreKeptLowercase_AndMustBeDomainNames(string domain, string? expected)
    {
        SsoConnection.NormalizeDomain(domain).ShouldBe(expected);
    }

    [Theory]
    [InlineData("grace@Acme.com", "acme.com")]
    [InlineData("a@b@acme.com", "acme.com")]
    [InlineData("acme.com", null)]
    [InlineData("grace@", null)]
    [InlineData(null, null)]
    public void DomainOf_ReadsTheDomainOfAnAddress(string? email, string? expected)
    {
        SsoConnection.DomainOf(email).ShouldBe(expected);
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/0f1e2d3c/v2.0", true)]
    [InlineData("https://acme.okta.com", true)]
    [InlineData("http://localhost:8080/realms/acme", true)]
    [InlineData("http://acme.okta.com", false)]
    [InlineData("https://acme.okta.com?tenant=1", false)]
    [InlineData("https://user:password@acme.okta.com", false)]
    [InlineData("acme.okta.com", false)]
    public void Issuers_AreHttpsUrls_ExceptOnTheLocalMachine(string issuer, bool valid)
    {
        SsoConnection.IsValidIssuer(issuer).ShouldBe(valid);
    }

    [Fact]
    public void Update_KeepsTheDomainsThatStay()
    {
        var connection = SsoConnection.Create(Guid.NewGuid(), SsoProtocol.OpenIdConnect, Oidc(["acme.com", "ACME.com", "acme.org"]), Now).Value;
        var kept = connection.Domains.Single(domain => domain.Domain == "acme.com");

        connection.Update(Oidc(["acme.com", "acme.io"]), Now).IsSuccess.ShouldBeTrue();

        connection.Domains.Select(domain => domain.Domain).Order().ShouldBe(["acme.com", "acme.io"]);
        connection.Domains.ShouldContain(kept);
        connection.Covers("grace@ACME.io").ShouldBeTrue();
        connection.Covers("grace@acme.org").ShouldBeFalse();
    }

    [Fact]
    public void Update_RequiresDomains()
    {
        var connection = SsoConnection.Create(Guid.NewGuid(), SsoProtocol.OpenIdConnect, Oidc(["acme.com"]), Now).Value;

        connection.Update(Oidc([]), Now).Error.ShouldBe(SsoErrors.InvalidDomains);
        connection.Update(Oidc(["acme.com", "not a domain"]), Now).Error.ShouldBe(SsoErrors.InvalidDomains);
        connection.Domains.ShouldHaveSingleItem().Domain.ShouldBe("acme.com");
    }

    [Fact]
    public void SamlConnections_NeedASignOnUrlAndACertificate_InPemOrBase64()
    {
        using var key = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=idp", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(Now.AddDays(-1), Now.AddYears(1));
        var settings = new SsoConnectionSettings("Acme AD FS", "http://adfs.acme.com/adfs/services/trust", ["acme.com"], Enabled: true)
        {
            SignOnUrl = "https://adfs.acme.com/adfs/ls/",
            Certificate = Convert.ToBase64String(certificate.RawData),
        };

        var connection = SsoConnection.Create(Guid.NewGuid(), SsoProtocol.Saml, settings, Now).Value;

        connection.Certificate.ShouldBe(certificate.ExportCertificatePem());
        connection.ClientId.ShouldBeNull();
        connection.Update(settings with { Certificate = certificate.ExportCertificatePem() }, Now).IsSuccess.ShouldBeTrue();
        connection.Update(settings with { Certificate = "not a certificate" }, Now).Error.ShouldBe(SsoErrors.InvalidCertificate);
        connection.Update(settings with { SignOnUrl = "http://adfs.acme.com/adfs/ls/" }, Now).Error.ShouldBe(SsoErrors.InvalidSignOnUrl);
        connection.Update(settings with { SignOnUrl = "https://accounts.google.com/o/saml2/idp?idpid=C01" }, Now).IsSuccess.ShouldBeTrue();
        connection.Update(settings with { Issuer = " " }, Now).Error.ShouldBe(SsoErrors.InvalidEntityId);
    }

    [Fact]
    public void LoginProviders_NameTheirConnection()
    {
        var id = Guid.NewGuid();

        SsoConnection.IdOf(SsoConnection.LoginProviderOf(id)).ShouldBe(id);
        SsoConnection.IdOf("google").ShouldBeNull();
        SsoConnection.IdOf("sso:not-an-id").ShouldBeNull();
    }

    [Theory]
    [InlineData("https://accounts.google.com", "acme.com", true)]
    [InlineData("https://accounts.google.com/", "ACME.com", true)]
    [InlineData("https://accounts.google.com", null, false)]
    [InlineData("https://accounts.google.com", "other.com", false)]
    [InlineData("https://acme.okta.com", null, true)]
    public void GoogleSignIns_MustBeOfTheOrganizationsWorkspace(string issuer, string? hostedDomain, bool accepted)
    {
        SsoConnection.IsOrganizationAccount(issuer, ["acme.com"], hostedDomain).ShouldBe(accepted);
    }
}
