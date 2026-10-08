using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages;

/// <summary>The landing page of the reset link in the email.</summary>
public sealed class ResetPasswordModel(
    ResetPasswordHandler resetPassword,
    AccountErrorMessages errorMessages,
    IOptions<AccountOptions> accounts) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public NewPasswordInput Input { get; set; } = new();

    public bool Completed { get; private set; }

    public bool LinkInvalid { get; private set; }

    public int PasswordMinimumLength => accounts.Value.PasswordMinimumLength;

    public void OnGet() => LinkInvalid = UserId == Guid.Empty || string.IsNullOrEmpty(Token);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (UserId == Guid.Empty || string.IsNullOrEmpty(Token))
        {
            LinkInvalid = true;
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await resetPassword.HandleAsync(new ResetPasswordCommand(UserId, Token, Input.Password), cancellationToken);

        if (result.IsSuccess)
        {
            Completed = true;
        }
        else if (result.Error == AccountErrors.InvalidLink)
        {
            LinkInvalid = true;
        }
        else
        {
            ModelState.AddModelError(errorMessages.FieldFor(result.Error), errorMessages.For(result.Error));
        }

        return Page();
    }
}

public sealed class NewPasswordInput
{
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
