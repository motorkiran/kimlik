using Kimlik.Application.Accounts;
using Kimlik.Domain.Common;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Identity;

/// <summary>Turns account errors into localized messages for the hosted pages.</summary>
public sealed class AccountErrorMessages(IStringLocalizer<SharedResource> localizer, IOptions<AccountOptions> accounts)
{
    public string For(Error error) => error switch
    {
        _ when error == AccountErrors.EmailAlreadyRegistered => localizer["An account with this email address already exists."],
        _ when error == AccountErrors.InvalidEmail => localizer["Enter a valid email address."],
        _ when error == AccountErrors.PasswordTooShort => localizer["Use at least {0} characters.", accounts.Value.PasswordMinimumLength],
        _ when error == AccountErrors.PasswordTooLong => localizer["Use at most {0} characters.", AccountOptions.PasswordMaximumLength],
        _ when error == AccountErrors.PasswordRejected => localizer["Choose a different password."],
        _ when error == AccountErrors.PasswordMissing => localizer["Choose a password."],
        _ when error == AccountErrors.RegistrationClosed => localizer["Registration is closed. Ask an administrator for an invitation."],
        _ when error == AccountErrors.WrongPassword => localizer["That password is not right."],
        _ when error == AccountErrors.LockedOut => localizer["Too many failed attempts. Try again later."],
        _ => localizer["Something went wrong. Try again."],
    };

    /// <summary>The form field an error belongs to; an empty name shows it above the form.</summary>
    public string FieldFor(Error error) => error switch
    {
        _ when error == AccountErrors.EmailAlreadyRegistered || error == AccountErrors.InvalidEmail => "Input.Email",
        _ when error == AccountErrors.PasswordTooShort || error == AccountErrors.PasswordTooLong || error == AccountErrors.PasswordRejected
            || error == AccountErrors.PasswordMissing => "Input.Password",
        _ when error == AccountErrors.WrongPassword => "Input.CurrentPassword",
        _ => string.Empty,
    };
}
