using System.Collections.Specialized;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel.Security;
using System.Text.Json;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Kimlik.Application.Accounts;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.SocialLogin;

/// <summary>A sign-in a browser started at a SAML provider, waiting for the provider's response.</summary>
/// <param name="ConnectionId">The connection it went to.</param>
/// <param name="RequestId">The ID of the authentication request, which the response must answer.</param>
/// <param name="ReturnUrl">Where the browser goes once signed in.</param>
/// <param name="ExpiresAt">When the provider must have answered by.</param>
public sealed record PendingSamlRequest(Guid ConnectionId, string RequestId, string ReturnUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// Kimlik as a SAML 2.0 service provider, one for every SAML connection, so that its values are known before a
/// connection exists. It sends unsigned authentication requests with the HTTP-Redirect binding, and takes responses with
/// the HTTP-POST binding only when they answer a request the same browser started, which a short-lived protected cookie
/// names: responses a provider starts on its own are refused.
/// </summary>
public sealed partial class SamlServiceProvider(
    IOptions<ServerOptions> server, IDataProtectionProvider dataProtection, TimeProvider timeProvider, ILogger<SamlServiceProvider> logger)
{
    /// <summary>Kimlik's entity ID, relative to the installation's URL; its other endpoints are below it.</summary>
    public const string Path = "signin/sso/saml";

    private const string CookieName = "kimlik.saml";
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The attributes providers put the address in, the claim types ITfoxtec reads them as.</summary>
    private static readonly string[] EmailAttributes =
    [
        ClaimTypes.Email, "email", "emailaddress", "mail", "urn:oid:0.9.2342.19200300.100.1.3", "http://schemas.xmlsoap.org/claims/EmailAddress",
    ];

    private static readonly string[] GivenNameAttributes = [ClaimTypes.GivenName, "given_name", "givenName", "firstName", "urn:oid:2.5.4.42"];
    private static readonly string[] FamilyNameAttributes = [ClaimTypes.Surname, "family_name", "sn", "surname", "lastName", "urn:oid:2.5.4.4"];

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Kimlik.Saml.PendingRequest");

    public string EntityId => $"{server.Value.PublicUrl!.AbsoluteUri.TrimEnd('/')}/{Path}";

    public Uri AssertionConsumerService => new($"{EntityId}/acs");

    /// <summary>Sends the browser to the connection's provider, and remembers the request in this browser.</summary>
    public IActionResult Challenge(HttpContext http, SsoProvider connection, string? returnUrl)
    {
        var request = new Saml2AuthnRequest(ConfigurationFor(connection))
        {
            AssertionConsumerServiceUrl = AssertionConsumerService,
            NameIdPolicy = new NameIdPolicy { AllowCreate = true },
        };
        var binding = new Saml2RedirectBinding();
        binding.Bind(request);

        var pending = new PendingSamlRequest(
            connection.Id, request.IdAsString, AccountLinks.IsLocalUrl(returnUrl) ? returnUrl! : "/", timeProvider.GetUtcNow() + RequestLifetime);
        http.Response.Cookies.Append(CookieName, _protector.Protect(JsonSerializer.Serialize(pending)), CookieOptions(http));
        return new RedirectResult(binding.RedirectLocation.OriginalString);
    }

    /// <summary>The request this browser is waiting on, if it has one that has not expired; it is used up.</summary>
    public PendingSamlRequest? TakePending(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var cookie))
        {
            return null;
        }

        // Used up whatever comes of it; without Max-Age, which would outlast the expiry that deletes it.
        http.Response.Cookies.Delete(CookieName, CookieOptions(http, lasting: false));
        try
        {
            var pending = JsonSerializer.Deserialize<PendingSamlRequest>(_protector.Unprotect(cookie));
            return pending is not null && pending.ExpiresAt > timeProvider.GetUtcNow() ? pending : null;
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Who the connection's provider says signed in, when the posted response is signed by its certificate, comes from
    /// its entity ID, is meant for Kimlik and answers <paramref name="pending"/>; <see langword="null"/> otherwise.
    /// </summary>
    public SsoIdentity? Read(IFormCollection form, SsoProvider connection, PendingSamlRequest pending)
    {
        var request = new ITfoxtec.Identity.Saml2.Http.HttpRequest
        {
            Method = HttpMethods.Post,
            Form = new NameValueCollection(),
            Query = new NameValueCollection(),
            QueryString = string.Empty,
        };
        foreach (var (name, value) in form)
        {
            request.Form.Add(name, value.ToString());
        }

        var response = new Saml2AuthnResponse(ConfigurationFor(connection));
        try
        {
            new Saml2PostBinding().Unbind(request, response);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRefused(logger, connection.Id, exception);
            return null;
        }

        if (response.Status != Saml2StatusCodes.Success
            || response.InResponseToAsString != pending.RequestId
            || (response.Destination is { } destination && destination != AssertionConsumerService)
            || response.ClaimsIdentity is not { } identity
            || identity.FindFirst(ClaimTypes.NameIdentifier)?.Value is not { Length: > 0 } subject)
        {
            LogUnanswered(logger, connection.Id);
            return null;
        }

        var email = First(identity, EmailAttributes) ?? (subject.Contains('@', StringComparison.Ordinal) ? subject : null);
        return new SsoIdentity(subject, email, First(identity, GivenNameAttributes), First(identity, FamilyNameAttributes), Locale: null);
    }

    /// <summary>Kimlik's metadata as a service provider, which providers can import.</summary>
    public string Metadata()
    {
        var descriptor = new EntityDescriptor(new Saml2Configuration { Issuer = EntityId }, signMetadata: false)
        {
            SPSsoDescriptor = new SPSsoDescriptor
            {
                AuthnRequestsSigned = false,
                WantAssertionsSigned = true,
                NameIDFormats = [NameIdentifierFormats.Email, NameIdentifierFormats.Persistent, NameIdentifierFormats.Unspecified],
                AssertionConsumerServices = [new AssertionConsumerService { Binding = ProtocolBindings.HttpPost, Location = AssertionConsumerService }],
            },
        };

        return new Saml2Metadata(descriptor).CreateMetadata().ToXml();
    }

    /// <summary>
    /// The connection seen from Kimlik: its provider's entity ID, sign-on URL and certificate, which is trusted as it is,
    /// without a chain, because the connection pins it.
    /// </summary>
    private Saml2Configuration ConfigurationFor(SsoProvider connection)
    {
        var configuration = new Saml2Configuration
        {
            Issuer = EntityId,
            SingleSignOnDestination = new Uri(connection.SignOnUrl!),
            AllowedIssuer = connection.Issuer,
            CertificateValidationMode = X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck,
        };
        configuration.SignatureValidationCertificates.Add(X509Certificate2.CreateFromPem(connection.Certificate!));
        configuration.AllowedAudienceUris.Add(EntityId);
        return configuration;
    }

    /// <summary>
    /// The cookie goes along when the provider posts its response from its own site, which takes <c>SameSite=None</c>
    /// and so HTTPS; over plain HTTP, in development, it is <c>Lax</c>, which providers that post back do not get.
    /// </summary>
    private CookieOptions CookieOptions(HttpContext http, bool lasting = true) => new()
    {
        HttpOnly = true,
        Path = $"/{Path}",
        Secure = server.Value.RequireHttps || http.Request.IsHttps,
        SameSite = server.Value.RequireHttps || http.Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
        MaxAge = lasting ? RequestLifetime : null,
        IsEssential = true,
    };

    private static string? First(ClaimsIdentity identity, string[] types) =>
        types.Select(type => identity.FindFirst(type)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    [LoggerMessage(LogLevel.Warning, "A SAML response for connection {ConnectionId} was refused")]
    private static partial void LogRefused(ILogger logger, Guid connectionId, Exception exception);

    [LoggerMessage(LogLevel.Warning, "A SAML response for connection {ConnectionId} failed or did not answer the request of the browser that sent it")]
    private static partial void LogUnanswered(ILogger logger, Guid connectionId);
}
