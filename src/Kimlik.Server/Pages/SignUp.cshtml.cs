using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages;

public sealed class SignUpModel(
    RegisterUserHandler registerUser,
    SignInManager<User> signInManager,
    AccountErrorMessages errorMessages,
    IOptions<AccountOptions> accounts) : PageModel
{
    [BindProperty]
    public SignUpInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool IsOpen => accounts.Value.Registration == RegistrationMode.Open;

    public int PasswordMinimumLength => accounts.Value.PasswordMinimumLength;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!IsOpen || !ModelState.IsValid)
        {
            return Page();
        }

        var command = new RegisterUserCommand(Input.Email, Input.Password, Input.GivenName, Input.FamilyName, CultureInfo.CurrentUICulture.Name);
        var result = await registerUser.HandleAsync(command, cancellationToken);

        if (result.IsSuccess)
        {
            if (accounts.Value.RequireVerifiedEmail)
            {
                return RedirectToPage("/SignUpComplete");
            }

            await signInManager.SignInAsync(result.Value, isPersistent: false);
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
        }

        // When addresses must be verified, a taken address gets the same answer as a new one,
        // so sign-up cannot be used to find out who has an account.
        if (result.Error == AccountErrors.EmailAlreadyRegistered && accounts.Value.RequireVerifiedEmail)
        {
            return RedirectToPage("/SignUpComplete");
        }

        ModelState.AddModelError(errorMessages.FieldFor(result.Error), errorMessages.For(result.Error));
        return Page();
    }
}

public sealed class SignUpInput
{
    [StringLength(User.NameMaxLength, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "First name")]
    public string? GivenName { get; set; }

    [StringLength(User.NameMaxLength, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Last name")]
    public string? FamilyName { get; set; }

    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(AccountOptions.PasswordMaximumLength, ErrorMessage = "Use at most {1} characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;
}
