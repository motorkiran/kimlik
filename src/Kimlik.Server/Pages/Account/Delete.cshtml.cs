using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages.Account;

/// <summary>
/// Deletes the account once the password confirms it, and signs this browser out. Accounts without a password confirm
/// by typing their email address.
/// </summary>
public sealed class DeleteModel(
    UserManager<User> userManager,
    MyAccount account,
    SignInManager<User> signInManager,
    RequestThrottle throttle,
    AccountErrorMessages errorMessages,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    [BindProperty]
    public DeleteAccountInput Input { get; set; } = new();

    public bool Deleted { get; private set; }

    /// <summary>Whether the password confirms the deletion, rather than the email address.</summary>
    public bool HasPassword { get; private set; }

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
            if (!string.Equals(Input.Email?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Email)}", localizer["Type the email address of your account."]);
            }
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

        var deleted = HasPassword
            ? await account.DeleteAsync(user.Id, new DeleteAccountRequest { Password = Input.CurrentPassword }, cancellationToken)
            : await account.DeleteWithoutPasswordAsync(user.Id, cancellationToken);
        if (deleted.IsFailure)
        {
            ModelState.AddModelError(errorMessages.FieldFor(deleted.Error), errorMessages.For(deleted.Error));
            return Page();
        }

        await signInManager.SignOutAsync();
        Deleted = true;
        return Page();
    }
}

public sealed class DeleteAccountInput
{
    [Required(ErrorMessage = "Enter your password.")]
    [StringLength(AccountOptions.PasswordMaximumLength, ErrorMessage = "Use at most {1} characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [StringLength(256, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Email")]
    public string? Email { get; set; }
}
