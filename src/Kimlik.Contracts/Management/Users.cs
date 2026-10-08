using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

public enum UserStatus
{
    Active,
    Suspended,
}

public sealed record UserResponse(
    Guid Id,
    string? Email,
    bool EmailVerified,
    string? GivenName,
    string? FamilyName,
    string? Name,
    string? Locale,
    UserStatus Status,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSignInAt);

/// <summary>Creates a user. Without a password, the user sets one through the password reset flow.</summary>
public sealed record CreateUserRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public required string Email { get; init; }

    [StringLength(100)]
    public string? GivenName { get; init; }

    [StringLength(100)]
    public string? FamilyName { get; init; }

    /// <summary>Preferred language as a BCP 47 tag, such as <c>tr</c> or <c>en</c>.</summary>
    [StringLength(16)]
    public string? Locale { get; init; }

    [StringLength(128, MinimumLength = 1)]
    public string? Password { get; init; }

    /// <summary>Marks the address as verified, for example when it was verified by another system.</summary>
    public bool EmailVerified { get; init; }
}

/// <summary>
/// Changes the profile with JSON Merge Patch semantics (RFC 7396): omitted properties keep their value and
/// <c>null</c> clears one.
/// </summary>
public sealed record UpdateUserRequest
{
    [StringLength(100)]
    public string? GivenName
    {
        get;
        init
        {
            field = value;
            HasGivenName = true;
        }
    }

    [StringLength(100)]
    public string? FamilyName
    {
        get;
        init
        {
            field = value;
            HasFamilyName = true;
        }
    }

    [StringLength(16)]
    public string? Locale
    {
        get;
        init
        {
            field = value;
            HasLocale = true;
        }
    }

    /// <summary>Whether the request sets <see cref="GivenName"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasGivenName { get; private init; }

    /// <summary>Whether the request sets <see cref="FamilyName"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasFamilyName { get; private init; }

    /// <summary>Whether the request sets <see cref="Locale"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasLocale { get; private init; }
}

/// <summary>An account at another provider, such as Google, that the user signs in with.</summary>
/// <param name="Provider">The provider's name in Kimlik, such as <c>google</c>.</param>
/// <param name="ProviderDisplayName">The provider's name for people, such as <c>Google</c>.</param>
public sealed record UserLoginResponse(string Provider, string ProviderDisplayName);

/// <summary>Replaces the global roles of a user or a client.</summary>
public sealed record SetRolesRequest
{
    [Required]
    public required IReadOnlyList<string> Roles { get; init; }
}
