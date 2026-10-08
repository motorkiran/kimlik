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

    /// <summary>Too many wrong passwords or codes in a row; the account accepts none until the lockout ends.</summary>
    public static readonly Error LockedOut = Error.Forbidden("account.locked_out", "Too many failed attempts. Try again later.");

    /// <summary>Hosted pages only: the account has a password, which confirms the request instead.</summary>
    public static readonly Error PasswordRequired = Error.Validation("account.password_required", "The account has a password; confirm with it.");

    public static readonly Error PasswordAlreadySet = Error.Conflict("account.password_already_set", "The account already has a password; change it instead.");

    public static readonly Error LoginInUse = Error.Conflict("account.login_in_use", "That account at the provider is linked to another user.");

    public static readonly Error ProviderAlreadyLinked = Error.Conflict(
        "account.provider_already_linked", "An account at this provider is already linked; unlink it first.");

    public static readonly Error LoginNotFound = Error.NotFound("account.login_not_found", "No account at this provider is linked.");

    /// <summary>Unlinking would leave the user without a way to sign in.</summary>
    public static readonly Error LastSignInMethod = Error.Validation(
        "account.last_sign_in_method", "Set a password or link another account first, so there is still a way to sign in.");

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
