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

/// <summary>Changes the password. This browser stays signed in; every other session and application is signed out.</summary>
public sealed class PasswordModel(
    MyAccount account,
    SignInManager<User> signInManager,
    RequestThrottle throttle,
    AccountErrorMessages errorMessages,
    IOptions<AccountOptions> accounts,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    [BindProperty]
    public ChangePasswordInput Input { get; set; } = new();

    public bool Changed { get; private set; }

    public int PasswordMinimumLength => accounts.Value.PasswordMinimumLength;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
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

        var changed = await account.ChangePasswordAsync(
            UserId,
            new ChangePasswordRequest { CurrentPassword = Input.CurrentPassword, NewPassword = Input.Password },
            cancellationToken);

        if (changed.IsFailure)
        {
            ModelState.AddModelError(errorMessages.FieldFor(changed.Error), errorMessages.For(changed.Error));
            return Page();
        }

        await KeepThisSessionAsync(signInManager);
        Changed = true;
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
