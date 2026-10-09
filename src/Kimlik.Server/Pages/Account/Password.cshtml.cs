using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages.Account;

/// <summary>
/// Changes the password. This browser stays signed in; every other session and application is signed out. Accounts
/// without a password set a first one instead; with email sign-in on, people can also remove their password.
/// </summary>
public sealed class PasswordModel(
    UserManager<User> userManager,
    MyAccount account,
    SignInFlow signInFlow,
    RequestThrottle throttle,
    AccountErrorMessages errorMessages,
    IOptions<AccountOptions> accounts,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    [BindProperty]
    public ChangePasswordInput Input { get; set; } = new();

    public bool Changed { get; private set; }

    public bool Removed { get; private set; }

    /// <summary>The current password, which confirms removing it.</summary>
    [BindProperty]
    [DataType(DataType.Password)]
    public string? RemovePassword { get; set; }

    /// <summary>Whether the password can go: people then sign in with codes sent by email.</summary>
    public bool CanRemove => HasPassword && accounts.Value.EmailSignIn;

    /// <summary>Whether the account has a password to change, rather than a first one to set.</summary>
    public bool HasPassword { get; private set; }

    public int PasswordMinimumLength => accounts.Value.PasswordMinimumLength;

    public async Task<IActionResult> OnGetAsync()
    {
        if (await userManager.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        HasPassword = await userManager.HasPasswordAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await userManager.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        HasPassword = await userManager.HasPasswordAsync(user);
        if (!HasPassword)
        {
            ModelState.Remove($"{nameof(Input)}.{nameof(Input.CurrentPassword)}");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ModelState.AddModelError(string.Empty, localizer["Too many attempts. Wait a minute and try again."]);
            return Page();
        }

        var changed = HasPassword
            ? await account.ChangePasswordAsync(
                user.Id, new ChangePasswordRequest { CurrentPassword = Input.CurrentPassword, NewPassword = Input.Password }, cancellationToken)
            : await account.AddPasswordAsync(user.Id, Input.Password, cancellationToken);

        if (changed.IsFailure)
        {
            ModelState.AddModelError(errorMessages.FieldFor(changed.Error), errorMessages.For(changed.Error));
            return Page();
        }

        await signInFlow.RenewAsync();
        Changed = true;
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken cancellationToken)
    {
        if (await userManager.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        // The change form's fields are not part of removing the password.
        ModelState.Clear();
        HasPassword = await userManager.HasPasswordAsync(user);
        if (!CanRemove)
        {
            return Page();
        }

        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ModelState.AddModelError(string.Empty, localizer["Too many attempts. Wait a minute and try again."]);
            return Page();
        }

        var removed = await account.RemovePasswordAsync(user.Id, RemovePassword ?? string.Empty, cancellationToken);
        if (removed.IsFailure)
        {
            ModelState.AddModelError(nameof(RemovePassword), errorMessages.For(removed.Error));
            return Page();
        }

        await signInFlow.RenewAsync();
        Removed = true;
        return Page();
    }
}

public sealed class ChangePasswordInput
{
    [Required(ErrorMessage = "Enter your current password.")]
    [StringLength(AccountOptions.PasswordMaximumLength, ErrorMessage = "Use at most {1} characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(AccountOptions.PasswordMaximumLength, ErrorMessage = "Use at most {1} characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string Password { get; set; } = string.Empty;

    [Compare(nameof(Password), ErrorMessage = "Enter the same password twice.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
