using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages;

public sealed class ResendVerificationModel(ResendEmailVerificationHandler resendVerification) : PageModel
{
    [BindProperty]
    public EmailInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool Sent { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await resendVerification.HandleAsync(
            new ResendEmailVerificationCommand(Input.Email, AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl : null),
            cancellationToken);

        Sent = true;
        return Page();
    }
}

public sealed class EmailInput
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}
