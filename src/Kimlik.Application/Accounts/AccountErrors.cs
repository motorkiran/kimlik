using Kimlik.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Application.Accounts;

public static class AccountErrors
{
    public static readonly Error RegistrationClosed = Error.Forbidden("account.registration_closed", "Registration is closed.");

    public static readonly Error EmailAlreadyRegistered = Error.Conflict("account.email_already_registered", "An account with this email address already exists.");

    public static readonly Error InvalidEmail = Error.Validation("account.invalid_email", "The email address is not valid.");

    public static readonly Error PasswordTooShort = Error.Validation("account.password_too_short", "The password is too short.");

    public static readonly Error PasswordTooLong = Error.Validation("account.password_too_long", "The password is too long.");

    public static readonly Error PasswordRejected = Error.Validation("account.password_rejected", "The password does not meet the password policy.");

    public static readonly Error WrongPassword = Error.Validation("account.wrong_password", "The password is not right.");

    public static readonly Error SessionNotFound = Error.NotFound("account.session_not_found", "The session does not exist.");

    /// <summary>A verification or reset link that is invalid, expired or already used.</summary>
    public static readonly Error InvalidLink = Error.Validation("account.invalid_link", "The link is invalid or has expired.");

    /// <summary>Translates ASP.NET Core Identity errors into Kimlik's stable error codes.</summary>
    public static Error FromIdentity(IEnumerable<IdentityError> errors)
    {
        var codes = errors.Select(error => error.Code).ToHashSet(StringComparer.Ordinal);
        var describer = new IdentityErrorDescriber();

        if (codes.Contains(describer.DuplicateEmail(string.Empty).Code) || codes.Contains(describer.DuplicateUserName(string.Empty).Code))
        {
            return EmailAlreadyRegistered;
        }

        if (codes.Contains(describer.InvalidEmail(string.Empty).Code))
        {
            return InvalidEmail;
        }

        if (codes.Contains(describer.PasswordTooShort(0).Code))
        {
            return PasswordTooShort;
        }

        if (codes.Contains(PasswordTooLongCode))
        {
            return PasswordTooLong;
        }

        return codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal))
            ? PasswordRejected
            : Error.Failure("account.identity_error", string.Join(", ", codes));
    }

    /// <summary>The Identity error code reported by Kimlik's maximum length password validator.</summary>
    public const string PasswordTooLongCode = "PasswordTooLong";
}
