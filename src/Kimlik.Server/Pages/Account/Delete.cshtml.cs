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

/// <summary>Deletes the account once the password confirms it, and signs this browser out.</summary>
public sealed class DeleteModel(
    MyAccount account,
    SignInManager<User> signInManager,
    RequestThrottle throttle,
    AccountErrorMessages errorMessages,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    [BindProperty]
    public DeleteAccountInput Input { get; set; } = new();

    public bool Deleted { get; private set; }

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

        var deleted = await account.DeleteAsync(UserId, new DeleteAccountRequest { Password = Input.CurrentPassword }, cancellationToken);
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
}
