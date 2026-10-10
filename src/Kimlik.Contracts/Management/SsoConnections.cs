using System.ComponentModel.DataAnnotations;

namespace Kimlik.Contracts.Management;

/// <summary>
/// An organization's own identity provider, which signs in the people whose addresses are in its <c>Domains</c>, over
/// OpenID Connect. While it is enabled, those people sign in only through it. Its client secret is never returned.
/// </summary>
public sealed record SsoConnectionResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Issuer,
    string ClientId,
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

    /// <summary>
    /// The provider's issuer URL, from which Kimlik discovers its endpoints and keys, such as
    /// <c>https://login.microsoftonline.com/{tenant}/v2.0</c> or <c>https://acme.okta.com</c>.
    /// </summary>
    [Required]
    [StringLength(512)]
    public required string Issuer { get; init; }

    /// <summary>Kimlik's client ID at the provider, where <c>{PublicUrl}/signin/sso/callback</c> is its redirect URI.</summary>
    [Required]
    [StringLength(256)]
    public required string ClientId { get; init; }

    [Required]
    [StringLength(1024)]
    public required string ClientSecret { get; init; }

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

    public IReadOnlyList<string>? Domains { get; init; }

    public bool? Enabled { get; init; }
}
