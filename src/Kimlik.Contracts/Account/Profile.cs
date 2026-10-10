using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Account;

/// <summary>
/// The signed-in user as they see themselves: with the public metadata, but not the private one, and the verified phone
/// number they sign in with by text message, in E.164.
/// </summary>
public sealed record ProfileResponse(
    Guid Id,
    string? Email,
    bool EmailVerified,
    string? GivenName,
    string? FamilyName,
    string? Name,
    string? Locale,
    string? PictureUrl,
    string? TimeZone,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSignInAt,
    JsonObject PublicMetadata,
    string? PhoneNumber);

/// <summary>
/// Changes the profile with JSON Merge Patch semantics (RFC 7396): omitted properties keep their value and
/// <c>null</c> clears one. Metadata is the application's, so only the Management API changes it.
/// </summary>
public sealed record UpdateProfileRequest
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

    /// <summary>An image of the user, as an absolute HTTP or HTTPS URL; <c>null</c> clears it.</summary>
    [StringLength(2048)]
    public string? PictureUrl
    {
        get;
        init
        {
            field = value;
            HasPictureUrl = true;
        }
    }

    /// <summary>The user's time zone as an IANA name, such as <c>Europe/Istanbul</c>; <c>null</c> clears it.</summary>
    [StringLength(64)]
    public string? TimeZone
    {
        get;
        init
        {
            field = value;
            HasTimeZone = true;
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

    /// <summary>Whether the request sets <see cref="PictureUrl"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasPictureUrl { get; private init; }

    /// <summary>Whether the request sets <see cref="TimeZone"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasTimeZone { get; private init; }

}
