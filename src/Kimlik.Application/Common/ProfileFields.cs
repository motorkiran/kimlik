using Kimlik.Domain.Common;
using Kimlik.Domain.Users;

namespace Kimlik.Application.Common;

/// <summary>Checks of the profile fields that users and organizations share.</summary>
internal static class ProfileFields
{
    public static readonly Error InvalidPictureUrl =
        Error.Validation("profile.invalid_picture_url", "The picture must be an absolute HTTP or HTTPS URL of up to 2048 characters.");

    public static readonly Error InvalidTimeZone =
        Error.Validation("profile.invalid_time_zone", "The time zone must be an IANA name, such as Europe/Istanbul.");

    /// <summary>The first problem with the values a request sets, or <see langword="null"/>; blank values clear the field.</summary>
    public static Error? Check(string? pictureUrl, string? timeZone = null) =>
        !IsPictureUrl(pictureUrl) ? InvalidPictureUrl : !IsTimeZone(timeZone) ? InvalidTimeZone : null;

    public static bool IsPictureUrl(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (value.Trim().Length <= User.PictureUrlMaxLength
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));

    public static bool IsTimeZone(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (value.Trim().Length <= User.TimeZoneMaxLength && TimeZoneInfo.TryFindSystemTimeZoneById(value.Trim(), out var zone) && zone.HasIanaId);
}
