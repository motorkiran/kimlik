using Kimlik.Domain.Organizations;

namespace Kimlik.Domain.Tests.Organizations;

public sealed class SsoConnectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

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
        var connection = SsoConnection.Create(Guid.NewGuid(), "Acme", "https://acme.okta.com", "kimlik", ["acme.com", "ACME.com", "acme.org"], enabled: true, Now).Value;
        var kept = connection.Domains.Single(domain => domain.Domain == "acme.com");

        connection.Update("Acme", "https://acme.okta.com", "kimlik", ["acme.com", "acme.io"], enabled: true, Now).IsSuccess.ShouldBeTrue();

        connection.Domains.Select(domain => domain.Domain).Order().ShouldBe(["acme.com", "acme.io"]);
        connection.Domains.ShouldContain(kept);
        connection.Covers("grace@ACME.io").ShouldBeTrue();
        connection.Covers("grace@acme.org").ShouldBeFalse();
    }

    [Fact]
    public void Update_RequiresDomains()
    {
        var connection = SsoConnection.Create(Guid.NewGuid(), "Acme", "https://acme.okta.com", "kimlik", ["acme.com"], enabled: true, Now).Value;

        connection.Update("Acme", "https://acme.okta.com", "kimlik", [], enabled: true, Now).Error.ShouldBe(SsoErrors.InvalidDomains);
        connection.Update("Acme", "https://acme.okta.com", "kimlik", ["acme.com", "not a domain"], enabled: true, Now).Error.ShouldBe(SsoErrors.InvalidDomains);
        connection.Domains.ShouldHaveSingleItem().Domain.ShouldBe("acme.com");
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
