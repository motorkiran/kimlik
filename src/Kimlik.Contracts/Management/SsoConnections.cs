using System.ComponentModel.DataAnnotations;

namespace Kimlik.Contracts.Management;

public enum SsoProtocol
{
    OpenIdConnect,
    Saml,
}

/// <summary>
/// An organization's own identity provider, which signs in the people whose addresses are in its <c>Domains</c>, over
/// OpenID Connect or SAML. While it is enabled, those people sign in only through it. An OpenID Connect connection has a
/// <c>ClientId</c>, whose secret is never returned; a SAML one has the provider's <c>SignOnUrl</c> and signing
/// <c>Certificate</c>, and its <c>Issuer</c> is the provider's entity ID.
/// </summary>
public sealed record SsoConnectionResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    SsoProtocol Protocol,
    string Issuer,
    string? ClientId,
    string? SignOnUrl,
    string? Certificate,
    DateTimeOffset? CertificateExpiresAt,
    IReadOnlyList<string> Domains,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateSsoConnectionRequest
{
    [Required]
    public required Guid OrganizationId { get; init; }

    /// <summary>A name for people, such as <c>Acme Entra ID</c>.</summary>
    [Required]
    [StringLength(100)]
    public required string Name { get; init; }

    /// <summary>OpenID Connect, by default, or SAML; it cannot change later.</summary>
    public SsoProtocol Protocol { get; init; } = SsoProtocol.OpenIdConnect;

    /// <summary>
    /// For OpenID Connect, the provider's issuer URL, from which Kimlik discovers its endpoints and keys, such as
    /// <c>https://login.microsoftonline.com/{tenant}/v2.0</c> or <c>https://acme.okta.com</c>. For SAML, the provider's
    /// entity ID.
    /// </summary>
    [Required]
    [StringLength(512)]
    public required string Issuer { get; init; }

    /// <summary>
    /// OpenID Connect only: Kimlik's client ID at the provider, where <c>{PublicUrl}/signin/sso/callback</c> is its
    /// redirect URI.
    /// </summary>
    [StringLength(256)]
    public string? ClientId { get; init; }

    /// <summary>OpenID Connect only.</summary>
    [StringLength(1024)]
    public string? ClientSecret { get; init; }

    /// <summary>SAML only: where the provider takes authentication requests, with the HTTP-Redirect binding.</summary>
    [StringLength(2048)]
    public string? SignOnUrl { get; init; }

    /// <summary>SAML only: the certificate the provider signs with, in PEM or base64.</summary>
    [StringLength(16384)]
    public string? Certificate { get; init; }

    /// <summary>The email domains whose addresses the provider signs in, such as <c>acme.com</c>; each in one connection at most.</summary>
    [Required]
    public required IReadOnlyList<string> Domains { get; init; }

    public bool Enabled { get; init; } = true;
}

/// <summary>Changes a connection with JSON Merge Patch semantics: omitted properties keep their value.</summary>
public sealed record UpdateSsoConnectionRequest
{
    [StringLength(100)]
    public string? Name { get; init; }

    [StringLength(512)]
    public string? Issuer { get; init; }

    [StringLength(256)]
    public string? ClientId { get; init; }

    /// <summary>A new client secret; the current one stays when omitted.</summary>
    [StringLength(1024)]
    public string? ClientSecret { get; init; }

    [StringLength(2048)]
    public string? SignOnUrl { get; init; }

    [StringLength(16384)]
    public string? Certificate { get; init; }

    public IReadOnlyList<string>? Domains { get; init; }

    public bool? Enabled { get; init; }
}
