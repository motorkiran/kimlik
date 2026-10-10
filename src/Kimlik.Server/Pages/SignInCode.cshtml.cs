using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages;

/// <summary>
/// Where people enter the code sent to their address or texted to their number. It looks the same whether an account
/// has them or not; a right code signs them in as a password would, with the second factor next if the account needs
/// one. The link in an emailed code fills the code in, in the browser that asked for it; elsewhere, the page shows the
/// code to enter there.
/// </summary>
public sealed class SignInCodeModel(
    EmailSignIn emailSignIn,
    PhoneSignIn phoneSignIn,
    PendingSignInCode pendingSignInCode,
    SignInFlow signInFlow,
    RequestThrottle throttle,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public SignInCodeInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public SignInCodeChannel Channel { get; private set; }

    /// <summary>The address, or the phone number, the code went to if an account has it.</summary>
    public string? Address { get; private set; }

    public string? Message { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>Whether the code came filled in, from the link in the email.</summary>
    public bool FilledIn { get; private set; }

    /// <summary>The code of a link opened in another browser than the one that asked for it, to enter there.</summary>
    public string? CodeForAnotherBrowser { get; private set; }

    public IActionResult OnGet(string? code)
    {
        // Only what a code looks like is shown back.
        code = code is { Length: OneTimeCodes.Length } && code.All(char.IsAsciiDigit) ? code : null;
        if (pendingSignInCode.Read(HttpContext) is not { } pending)
        {
            if (code is null)
            {
                return RedirectToPage("/SignIn", new { ReturnUrl });
            }

            CodeForAnotherBrowser = code;
            return Page();
        }

        Show(pending);
        if (code is not null)
        {
            Input.Code = code;
            FilledIn = true;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (pendingSignInCode.Read(HttpContext) is not { } pending)
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        Show(pending);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            return TooManyAttempts();
        }

        Result<User> verified = pending.Channel == SignInCodeChannel.Sms
            ? await phoneSignIn.VerifyAsync(pending.Address, Input.Code, cancellationToken)
            : await emailSignIn.VerifyAsync(pending.Address, Input.Code, cancellationToken);
        if (verified.IsFailure)
        {
            ErrorMessage = localizer["That code is not right, or it has expired. Check it, or send a new one."];
            return Page();
        }

        pendingSignInCode.End(HttpContext);
        var returnUrl = AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
        var firstFactor = pending.Channel == SignInCodeChannel.Sms ? SignInFlow.SmsMethod : SignInFlow.EmailMethod;
        return LocalRedirect(await signInFlow.ContinueAsync(verified.Value, pending.Persistent, provider: null, returnUrl, cancellationToken, firstFactor));
    }

    public async Task<IActionResult> OnPostResendAsync(CancellationToken cancellationToken)
    {
        if (pendingSignInCode.Read(HttpContext) is not { } pending)
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        // The code field is not part of asking for a new code.
        ModelState.Clear();
        Show(pending);
        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            return TooManyAttempts();
        }

        if (pending.Channel == SignInCodeChannel.Sms)
        {
            await phoneSignIn.RequestAsync(pending.Address, cancellationToken);
            Message = localizer["If an account has this number, a new code is on its way. Codes can be sent once a minute."];
        }
        else
        {
            await emailSignIn.RequestAsync(pending.Address, cancellationToken);
            Message = localizer["If an account has this address, a new code is on its way. Codes can be sent once a minute."];
        }

        return Page();
    }

    private void Show(PendingSignInCode.Pending pending)
    {
        Channel = pending.Channel;
        Address = pending.Address;
        ReturnUrl ??= pending.ReturnUrl;
    }

    private PageResult TooManyAttempts()
    {
        Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ErrorMessage = localizer["Too many attempts. Wait a minute and try again."];
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
