using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages;

public sealed class ResendVerificationModel(
    ResendEmailVerificationHandler resendVerification,
    RequestThrottle throttle,
    IStringLocalizer<SharedResource> localizer) : PageModel
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

        if (!throttle.TryAcquire(ThrottledAction.EmailRequest, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ModelState.AddModelError(string.Empty, localizer["Too many attempts. Wait a minute and try again."]);
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
