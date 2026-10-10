using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens.Saml2;

namespace Kimlik.Server.Tests.SocialLogin;

/// <summary>An authentication request as Kimlik sent it to the provider, in the HTTP-Redirect binding.</summary>
internal sealed record SamlRequest(string Id, string Issuer, string AssertionConsumerService);

/// <summary>
/// An organization's SAML provider for the tests, such as AD FS. The browser never reaches it: tests read the request
/// Kimlik sends the browser with, and post back a response that <see cref="Respond"/> made, signed with the provider's
/// certificate unless told otherwise.
/// </summary>
internal sealed class TestSamlProvider : IDisposable
{
    public const string EntityId = "https://saml.test/acme";
    public const string SignOnUrl = "https://saml.test/acme/sso";

    private readonly RSA _key = RSA.Create(2048);

    public TestSamlProvider() =>
        Certificate = new CertificateRequest("CN=saml.test", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

    /// <summary>The certificate the provider signs with, which has its private key.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>Reads the request Kimlik sent the browser to the provider with.</summary>
    public static SamlRequest ReadRequest(HttpResponseMessage toProvider)
    {
        var location = toProvider.Headers.Location ?? throw new InvalidOperationException($"Expected a redirect to the provider, got {(int)toProvider.StatusCode}.");
        location.GetLeftPart(UriPartial.Path).ShouldBe(SignOnUrl);

        var encoded = QueryHelpers.ParseQuery(location.Query)["SAMLRequest"].ToString();
        using var inflated = new DeflateStream(new MemoryStream(Convert.FromBase64String(encoded)), CompressionMode.Decompress);
        var request = XDocument.Load(inflated).Root!;
        XNamespace assertion = "urn:oasis:names:tc:SAML:2.0:assertion";
        return new SamlRequest(
            request.Attribute("ID")!.Value,
            request.Element(assertion + "Issuer")!.Value,
            request.Attribute("AssertionConsumerServiceURL")!.Value);
    }

    /// <summary>
    /// A response to <paramref name="request"/> for the person with <paramref name="email"/>, as the form field
    /// <c>SAMLResponse</c> carries it. The arguments break it in the ways the tests need.
    /// </summary>
    public string Respond(
        SamlRequest request, string subject, string? email, X509Certificate2? signedWith = null, bool signed = true, string? inResponseTo = null, string? audience = null)
    {
        var configuration = new Saml2Configuration { Issuer = EntityId, SigningCertificate = signed ? signedWith ?? Certificate : null };
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.GivenName, "Grace")];
        if (email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        var response = new Saml2AuthnResponse(configuration)
        {
            InResponseToAsString = inResponseTo ?? request.Id,
            Status = Saml2StatusCodes.Success,
            Destination = new Uri(request.AssertionConsumerService),
            NameId = new Saml2NameIdentifier(subject, NameIdentifierFormats.Persistent),
            ClaimsIdentity = new ClaimsIdentity(claims),
        };
        response.CreateSecurityToken(audience ?? request.Issuer);

        var binding = new Saml2PostBinding();
        binding.Bind(response);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(binding.XmlDocument.OuterXml));
    }

    /// <summary>Posts a response to Kimlik's assertion consumer service, as the provider's page makes the browser do.</summary>
    public static async Task<HttpResponseMessage> PostAsync(Browser browser, SamlRequest request, string samlResponse)
    {
        using var form = new FormUrlEncodedContent([new("SAMLResponse", samlResponse)]);
        return await browser.Client.PostAsync(new Uri(request.AssertionConsumerService).PathAndQuery, form, TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        Certificate.Dispose();
        _key.Dispose();
    }
}
