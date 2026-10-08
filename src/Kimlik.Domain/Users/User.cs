using Microsoft.AspNetCore.Identity;

namespace Kimlik.Domain.Users;

/// <summary>
/// A person who can sign in. Credentials (password hash, lockout, security stamp) are managed by
/// ASP.NET Core Identity; the profile and the account lifecycle belong to Kimlik.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    public const int NameMaxLength = 100;
    public const int LocaleMaxLength = 16;
    public const int PictureUrlMaxLength = 2048;
    public const int TimeZoneMaxLength = 64;
    public const string EmptyMetadata = "{}";

    // Used by EF Core.
    private User()
    {
    }

    public string? GivenName { get; private set; }

    public string? FamilyName { get; private set; }

    /// <summary>Preferred UI and email language as a BCP 47 tag, such as <c>tr</c> or <c>en</c>.</summary>
    public string? Locale { get; private set; }

    /// <summary>An image of the person, as an absolute HTTP or HTTPS URL.</summary>
    public string? PictureUrl { get; private set; }

    /// <summary>The person's time zone as an IANA name, such as <c>Europe/Istanbul</c>.</summary>
    public string? TimeZone { get; private set; }

    public UserStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? LastSignInAt { get; private set; }

    /// <summary>A JSON object the application keeps about the user, which the user can read too.</summary>
    public string PublicMetadata { get; private set; } = EmptyMetadata;

    /// <summary>A JSON object the application keeps about the user, for its backend only.</summary>
    public string PrivateMetadata { get; private set; } = EmptyMetadata;

    /// <summary>The full name, or <see langword="null"/> when no name part is known.</summary>
    public string? Name => (GivenName, FamilyName) switch
    {
        (null, null) => null,
        (not null, null) => GivenName,
        (null, not null) => FamilyName,
        _ => $"{GivenName} {FamilyName}",
    };

    public static User Create(string email, string? givenName, string? familyName, string? locale, DateTimeOffset now)
    {
        var id = Guid.CreateVersion7(now);

        return new User
        {
            Id = id,
            // People sign in with their email address; the user name is an internal, immutable handle.
            UserName = id.ToString("N"),
            Email = email.Trim(),
            GivenName = NullIfBlank(givenName),
            FamilyName = NullIfBlank(familyName),
            Locale = NullIfBlank(locale),
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public bool CanSignIn => Status == UserStatus.Active;

    /// <summary>Replaces the profile; the caller has checked the picture URL and the time zone.</summary>
    public void UpdateProfile(string? givenName, string? familyName, string? locale, string? pictureUrl, string? timeZone, DateTimeOffset now)
    {
        GivenName = NullIfBlank(givenName);
        FamilyName = NullIfBlank(familyName);
        Locale = NullIfBlank(locale);
        PictureUrl = NullIfBlank(pictureUrl);
        TimeZone = NullIfBlank(timeZone);
        UpdatedAt = now;
    }

    /// <summary>Replaces the metadata; the caller has checked that each is a JSON object.</summary>
    public void SetMetadata(string publicMetadata, string privateMetadata, DateTimeOffset now)
    {
        PublicMetadata = publicMetadata;
        PrivateMetadata = privateMetadata;
        UpdatedAt = now;
    }

    public void Suspend(DateTimeOffset now)
    {
        Status = UserStatus.Suspended;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        Status = UserStatus.Active;
        UpdatedAt = now;
    }

    public void MarkEmailVerified(DateTimeOffset now)
    {
        EmailConfirmed = true;
        UpdatedAt = now;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
