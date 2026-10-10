using Kimlik.Application.Accounts;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Identity;

/// <summary>Turns account and organization errors into localized messages for the hosted pages.</summary>
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
        _ when error == AccountErrors.PasswordBreached => localizer["This password has appeared in a data breach. Choose a different one."],
        _ when error == AccountErrors.RegistrationClosed => localizer["Registration is closed. Ask an administrator for an invitation."],
        _ when error == AccountErrors.WrongPassword => localizer["That password is not right."],
        _ when error == AccountErrors.LockedOut => localizer["Too many failed attempts. Try again later."],
        _ when error == AccountErrors.InvalidPhoneNumber => localizer["Enter a phone number with its country code, such as +90 532 123 45 67."],
        _ when error == AccountErrors.PhoneNumberNotAllowed => localizer["Text messages cannot be sent to numbers in that country."],
        _ when error == AccountErrors.PhoneNumberUnchanged => localizer["That number is already on your account."],
        _ when error == AccountErrors.PhoneNumberInUse => localizer["That number is on another account."],
        _ when error == AccountErrors.TooManyTexts => localizer["A code was sent moments ago. Wait a minute before asking for another."],
        _ when error == AccountErrors.WrongCode => localizer["That code is not right, or it has expired. Check it, or send a new one."],
        _ when error == OrganizationErrors.InvalidName => localizer["Enter a name of at most 100 characters."],
        _ when error == OrganizationErrors.InvalidSlug => localizer["Use lowercase letters, digits and inner hyphens, such as acme."],
        _ when error == OrganizationErrors.SlugTaken => localizer["Another organization has this short name."],
        _ when error == OrganizationErrors.CreationDisabled => localizer["Only administrators can create organizations here."],
        _ when error == OrganizationErrors.MissingPermission => localizer["Your roles in the organization do not allow this."],
        _ when error == OrganizationErrors.PrivilegeEscalation =>
            localizer["You can only give roles whose permissions you hold, and only manage members who hold no more than you."],
        _ when error == OrganizationErrors.LastAdministrator => localizer["Someone else must be able to manage the members before you leave."],
        _ when error == OrganizationErrors.AlreadyMember => localizer["That person is already a member."],
        _ when error == OrganizationErrors.InvitationPending => localizer["That address already has an open invitation. Send it again instead."],
        _ when error == OrganizationErrors.InvitationClosed || error == OrganizationErrors.InvitationNotFound =>
            localizer["This invitation is no longer valid. Ask the organization to send a new one."],
        _ => localizer["Something went wrong. Try again."],
    };

    /// <summary>The form field an error belongs to; an empty name shows it above the form.</summary>
    public string FieldFor(Error error) => error switch
    {
        _ when error == AccountErrors.EmailAlreadyRegistered || error == AccountErrors.InvalidEmail => "Input.Email",
        _ when error == AccountErrors.PasswordTooShort || error == AccountErrors.PasswordTooLong || error == AccountErrors.PasswordRejected
            || error == AccountErrors.PasswordMissing || error == AccountErrors.PasswordBreached => "Input.Password",
        _ when error == AccountErrors.WrongPassword => "Input.CurrentPassword",
        _ when error == OrganizationErrors.InvalidName => "Input.Name",
        _ when error == OrganizationErrors.InvalidSlug || error == OrganizationErrors.SlugTaken => "Input.Slug",
        _ => string.Empty,
    };
}
