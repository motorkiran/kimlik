using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kimlik.Domain.Common;

namespace Kimlik.Domain.Organizations;

/// <summary>How a connection signs people in.</summary>
public enum SsoProtocol
{
    OpenIdConnect,
    Saml,
}

/// <summary>
/// What a connection is set to. <c>ClientId</c> is for OpenID Connect; <c>SignOnUrl</c> and <c>Certificate</c> are for
/// SAML, whose <c>Issuer</c> is the provider's entity ID.
/// </summary>
public sealed record SsoConnectionSettings(string Name, string Issuer, IEnumerable<string> Domains, bool Enabled)
{
    public string? ClientId { get; init; }

    public string? SignOnUrl { get; init; }

    public string? Certificate { get; init; }
}

/// <summary>
/// An organization's own identity provider, which signs in the people whose addresses are in its domains, over OpenID
/// Connect or SAML. While it is enabled, those people sign in only through it.
/// </summary>
public sealed class SsoConnection
{
    public const int NameMaxLength = 100;
    public const int IssuerMaxLength = 512;
    public const int ClientIdMaxLength = 256;
    public const int ClientSecretMaxLength = 1024;
    public const int SignOnUrlMaxLength = 2048;
    public const int CertificateMaxLength = 16384;
    public const int DomainMaxLength = 253;
    public const int MaxDomains = 20;

    private const string LoginProviderPrefix = "sso:";

    private readonly List<SsoDomain> _domains = [];

    // Used by EF Core.
    private SsoConnection()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganizationId { get; private init; }

    public string Name { get; private set; } = string.Empty;

    public SsoProtocol Protocol { get; private init; }

    /// <summary>
    /// The provider: for OpenID Connect its issuer URL, from which its endpoints and keys are discovered; for SAML its
    /// entity ID.
    /// </summary>
    public string Issuer { get; private set; } = string.Empty;

    /// <summary>Kimlik's client ID at an OpenID Connect provider.</summary>
    public string? ClientId { get; private set; }

    /// <summary>Kimlik's client secret at an OpenID Connect provider, encrypted with the master key.</summary>
    public string? EncryptedClientSecret { get; private set; }

    /// <summary>Where a SAML provider takes authentication requests.</summary>
    public string? SignOnUrl { get; private set; }

    /// <summary>The certificate a SAML provider signs its responses with, in PEM.</summary>
    public string? Certificate { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The email domains whose addresses the provider signs in, such as <c>acme.com</c>.</summary>
    public IReadOnlyCollection<SsoDomain> Domains => _domains;

    /// <summary>
    /// The name the connection's sign-ins go by: the provider of the users' linked logins and of their sessions, and
    /// the OpenIddict client registration that signs in with it.
    /// </summary>
    public string LoginProvider => LoginProviderOf(Id);

    public static string LoginProviderOf(Guid connectionId) => $"{LoginProviderPrefix}{connectionId:N}";

    /// <summary>The connection a login provider name stands for, when it stands for one.</summary>
    public static Guid? IdOf(string? loginProvider) =>
        loginProvider is not null && loginProvider.StartsWith(LoginProviderPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(loginProvider[LoginProviderPrefix.Length..], "N", out var id)
            ? id
            : null;

    /// <summary>A new connection; the caller sets the secret of an OpenID Connect one.</summary>
    public static Result<SsoConnection> Create(Guid organizationId, SsoProtocol protocol, SsoConnectionSettings settings, DateTimeOffset now)
    {
        var connection = new SsoConnection { Id = Guid.CreateVersion7(now), OrganizationId = organizationId, Protocol = protocol, CreatedAt = now };
        var updated = connection.Update(settings, now);
        return updated.IsSuccess ? connection : updated.Error;
    }

    /// <summary>Changes the connection; its protocol stays.</summary>
    public Result Update(SsoConnectionSettings settings, DateTimeOffset now)
    {
        if (settings.Name is null || settings.Name.Trim().Length is 0 or > NameMaxLength)
        {
            return SsoErrors.InvalidName;
        }

        string? certificate = null;
        if (Protocol == SsoProtocol.OpenIdConnect)
        {
            if (!IsValidIssuer(settings.Issuer))
            {
                return SsoErrors.InvalidIssuer;
            }

            if (settings.ClientId is null || settings.ClientId.Trim().Length is 0 or > ClientIdMaxLength)
            {
                return SsoErrors.InvalidClientId;
            }
        }
        else
        {
            if (settings.Issuer is null || settings.Issuer.Trim().Length is 0 or > IssuerMaxLength)
            {
                return SsoErrors.InvalidEntityId;
            }

            if (!IsValidSignOnUrl(settings.SignOnUrl))
            {
                return SsoErrors.InvalidSignOnUrl;
            }

            if ((certificate = NormalizeCertificate(settings.Certificate)) is null)
            {
                return SsoErrors.InvalidCertificate;
            }
        }

        var normalized = settings.Domains.Select(NormalizeDomain).ToList();
        if (normalized.Count is 0 or > MaxDomains || normalized.Any(domain => domain is null))
        {
            return SsoErrors.InvalidDomains;
        }

        Name = settings.Name.Trim();
        Issuer = settings.Issuer.Trim();
        ClientId = Protocol == SsoProtocol.OpenIdConnect ? settings.ClientId!.Trim() : null;
        SignOnUrl = Protocol == SsoProtocol.Saml ? settings.SignOnUrl : null;
        Certificate = certificate;
        Enabled = settings.Enabled;
        SetDomains(normalized.OfType<string>().Distinct(StringComparer.Ordinal));
        UpdatedAt = now;
        return Result.Success();
    }

    public void SetClientSecret(string encryptedClientSecret, DateTimeOffset now)
    {
        EncryptedClientSecret = encryptedClientSecret;
        UpdatedAt = now;
    }

    /// <summary>Google's issuer, which signs in personal Google accounts as well as an organization's Workspace accounts.</summary>
    public const string GoogleIssuer = "https://accounts.google.com";

    public bool Covers(string? email) => DomainOf(email) is { } domain && _domains.Any(candidate => candidate.Domain == domain);

    /// <summary>
    /// Whether a provider's sign-in is of an account the organization manages. Google signs in personal accounts too,
    /// whose addresses may be in the organization's domains, so its sign-ins must name one of the connection's domains as
    /// their Workspace domain (<c>hd</c>).
    /// </summary>
    public static bool IsOrganizationAccount(string issuer, IEnumerable<string> domains, string? hostedDomain) =>
        !Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Path).TrimEnd('/') != GoogleIssuer
        || (NormalizeDomain(hostedDomain) is { } domain && domains.Contains(domain, StringComparer.Ordinal));

    /// <summary>
    /// A domain as connections keep it, lowercase and without a final dot, such as <c>acme.com</c>; <see langword="null"/>
    /// when it is not a domain name with at least two labels.
    /// </summary>
    public static string? NormalizeDomain(string? domain)
    {
        var normalized = domain?.Trim().TrimEnd('.').ToLowerInvariant();
        return normalized is { Length: > 0 and <= DomainMaxLength }
            && normalized.Contains('.', StringComparison.Ordinal)
            && Uri.CheckHostName(normalized) == UriHostNameType.Dns
            ? normalized
            : null;
    }

    /// <summary>The domain of an email address, as connections keep domains.</summary>
    public static string? DomainOf(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        return at > 0 ? NormalizeDomain(email![(at + 1)..]) : null;
    }

    /// <summary>
    /// An absolute HTTPS URL without credentials, query or fragment. Plain HTTP is accepted for the local machine only,
    /// for development.
    /// </summary>
    public static bool IsValidIssuer(string? issuer) =>
        issuer is { Length: <= IssuerMaxLength }
        && Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IsLocal(uri)));

    /// <summary>
    /// An absolute HTTPS URL without credentials or fragment, which may carry a query, as Google's does. Plain HTTP is
    /// accepted for the local machine only, for development.
    /// </summary>
    public static bool IsValidSignOnUrl(string? url) =>
        url is { Length: <= SignOnUrlMaxLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IsLocal(uri)));

    /// <summary>
    /// A certificate as connections keep it, in PEM. It may come in PEM or as the base64 that providers show;
    /// <see langword="null"/> when it is not a certificate.
    /// </summary>
    public static string? NormalizeCertificate(string? certificate)
    {
        if (string.IsNullOrWhiteSpace(certificate) || certificate.Length > CertificateMaxLength)
        {
            return null;
        }

        try
        {
            using var parsed = certificate.Contains("-----BEGIN", StringComparison.Ordinal)
                ? X509Certificate2.CreateFromPem(certificate)
                : X509CertificateLoader.LoadCertificate(Convert.FromBase64String(string.Concat(certificate.Where(character => !char.IsWhiteSpace(character)))));
            return parsed.ExportCertificatePem();
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsLocal(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));

    /// <summary>Keeps the domains that stay, so that only the changes reach the database.</summary>
    private void SetDomains(IEnumerable<string> domains)
    {
        var wanted = domains.ToHashSet(StringComparer.Ordinal);
        _domains.RemoveAll(domain => !wanted.Contains(domain.Domain));
        _domains.AddRange(wanted.Where(domain => _domains.All(kept => kept.Domain != domain)).Order(StringComparer.Ordinal).Select(domain => new SsoDomain(Id, domain)));
    }
}

/// <summary>An email domain that a connection covers; a domain belongs to one connection at most.</summary>
public sealed class SsoDomain(Guid connectionId, string domain)
{
    public Guid ConnectionId { get; private init; } = connectionId;

    public string Domain { get; private init; } = domain;
}
