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

    public static readonly Error PasswordBreached = Error.Validation(
        "account.password_breached", "The password has appeared in a data breach; choose another.");

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

    /// <summary>An organization's provider signed someone in for the first time without an address in the connection's domains.</summary>
    public static readonly Error AddressOutsideConnection = Error.Validation(
        "account.address_outside_connection", "The provider did not share an email address in the organization's domains.");

    /// <summary>The code is wrong, has expired or was replaced by a newer one, or no account can sign in with it.</summary>
    public static readonly Error WrongCode = Error.Validation("account.wrong_code", "The code is not right, or it has expired.");

    public static readonly Error PasswordMissing = Error.Validation("account.password_missing", "Choose a password.");

    public static readonly Error EmailSignInOff = Error.Validation(
        "account.email_sign_in_off", "Signing in with codes sent by email is turned off, so the password stays.");

    public static readonly Error PasskeyNotFound = Error.NotFound("account.passkey_not_found", "The user has no such passkey.");

    public static readonly Error TooManyPasskeys = Error.Conflict("account.too_many_passkeys", "A user can have up to 25 passkeys; remove one first.");

    /// <summary>The browser's answer to a passkey ceremony was not valid, or came too late.</summary>
    public static readonly Error PasskeyRejected = Error.Validation("account.passkey_rejected", "The passkey could not be verified. Try again.");

    /// <summary>Unlinking would leave the user without a way to sign in.</summary>
    public static readonly Error LastSignInMethod = Error.Validation(
        "account.last_sign_in_method", "Set a password or link another account first, so there is still a way to sign in.");

    /// <summary>An administrator acting as the user, for support, sees the account but changes nothing.</summary>
    public static readonly Error Impersonating = Error.Forbidden(
        "account.impersonating", "An administrator acting as the user cannot change their account.");

    public static readonly Error SmsOff = Error.Validation("account.sms_off", "Text messages are not set up, so phone numbers cannot be used.");

    public static readonly Error InvalidPhoneNumber = Error.Validation(
        "account.invalid_phone_number", "Enter a phone number with its country code, such as +90 532 123 45 67.");

    public static readonly Error PhoneNumberNotAllowed = Error.Validation(
        "account.phone_number_not_allowed", "Text messages cannot be sent to numbers in that country.");

    public static readonly Error PhoneNumberUnchanged = Error.Validation("account.phone_number_unchanged", "That number is already verified on the account.");

    public static readonly Error PhoneNumberInUse = Error.Conflict("account.phone_number_in_use", "That number is verified on another account.");

    public static readonly Error PhoneNumberMissing = Error.NotFound("account.phone_number_missing", "The account has no phone number.");

    /// <summary>A code went to the account moments ago, or too many in the last hour.</summary>
    public static readonly Error TooManyTexts = Error.Conflict(
        "account.too_many_texts", "A code was sent moments ago. Wait a minute, or an hour after several, before asking for another.");

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

        if (codes.Contains(PasswordBreachedCode))
        {
            return PasswordBreached;
        }

        return codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal))
            ? PasswordRejected
            : Error.Failure("account.identity_error", string.Join(", ", codes));
    }

    /// <summary>The Identity error code reported by Kimlik's maximum length password validator.</summary>
    public const string PasswordTooLongCode = "PasswordTooLong";

    /// <summary>The Identity error code reported when a password appears in a known data breach.</summary>
    public const string PasswordBreachedCode = "PasswordBreached";
}
