using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages;

/// <summary>
/// Signing in with a code texted to the account's verified phone number, when text messages are set up. Like the
/// email code, it asks for the code whether an account has the number or not.
/// </summary>
public sealed class SignInPhoneModel(
    PhoneSignIn phoneSignIn,
    PendingSignInCode pendingSignInCode,
    RequestThrottle throttle,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public SignInPhoneInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet() => phoneSignIn.Enabled ? Page() : RedirectToPage("/SignIn", new { ReturnUrl });

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!phoneSignIn.Enabled)
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (phoneSignIn.Normalize(Input.PhoneNumber) is not { } phoneNumber)
        {
            ModelState.AddModelError("Input.PhoneNumber", localizer["Enter a phone number with its country code, such as +90 532 123 45 67."]);
            return Page();
        }

        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ErrorMessage = localizer["Too many attempts. Wait a minute and try again."];
            return Page();
        }

        await phoneSignIn.RequestAsync(phoneNumber, cancellationToken);
        pendingSignInCode.Start(HttpContext, SignInCodeChannel.Sms, phoneNumber, Input.RememberMe, ReturnUrl);
        return RedirectToPage("/SignInCode", new { ReturnUrl });
    }
}

public sealed class SignInPhoneInput
{
    [Required(ErrorMessage = "Enter your phone number.")]
    [StringLength(32, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Phone number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }
}
