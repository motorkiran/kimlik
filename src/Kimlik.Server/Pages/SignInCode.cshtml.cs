using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages;

/// <summary>
/// Where people enter the code sent to their address. It looks the same whether an account has the address or not;
/// a right code signs them in as a password would, with the second factor next if the account needs one.
/// </summary>
public sealed class SignInCodeModel(
    EmailSignIn emailSignIn,
    PendingEmailCode pendingEmailCode,
    SignInFlow signInFlow,
    RequestThrottle throttle,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public SignInCodeInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Email { get; private set; }

    public string? Message { get; private set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (pendingEmailCode.Read(HttpContext) is not { } pending)
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        Email = pending.Email;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (pendingEmailCode.Read(HttpContext) is not { } pending)
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        Email = pending.Email;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ErrorMessage = localizer["Too many attempts. Wait a minute and try again."];
            return Page();
        }

        var verified = await emailSignIn.VerifyAsync(pending.Email, Input.Code, cancellationToken);
        if (verified.IsFailure)
        {
            ErrorMessage = localizer["That code is not right, or it has expired. Check it, or send a new one."];
            return Page();
        }

        pendingEmailCode.End(HttpContext);
        var returnUrl = AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
        return LocalRedirect(await signInFlow.ContinueAsync(
            verified.Value, pending.Persistent, provider: null, returnUrl, cancellationToken, SignInFlow.EmailMethod));
    }

    public async Task<IActionResult> OnPostResendAsync(CancellationToken cancellationToken)
    {
        if (pendingEmailCode.Read(HttpContext) is not { } pending)
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        // The code field is not part of asking for a new code.
        ModelState.Clear();
        Email = pending.Email;
        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ErrorMessage = localizer["Too many attempts. Wait a minute and try again."];
            return Page();
        }

        await emailSignIn.RequestAsync(pending.Email, cancellationToken);
        Message = localizer["If an account has this address, a new code is on its way. Codes can be sent once a minute."];
        return Page();
    }
}

public sealed class SignInCodeInput
{
    [Required(ErrorMessage = "Enter the code.")]
    [StringLength(16, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;
}
