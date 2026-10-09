using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Mfa;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages;

/// <summary>
/// Sets up an authenticator app during sign-in, for users whom the policy requires to have a second factor. The
/// session starts once a code from the app confirms it, and the recovery codes are shown once.
/// </summary>
public sealed class SignInSetUpTwoFactorModel(
    SignInFlow signInFlow,
    TwoFactor twoFactor,
    RequestThrottle throttle,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Enter the code.")]
    [StringLength(16)]
    [Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;

    public AuthenticatorKeyView? Key { get; private set; }

    /// <summary>Set once two-factor authentication is on: the codes to save.</summary>
    public IReadOnlyList<string>? RecoveryCodes { get; private set; }

    public string ContinueUrl => AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await signInFlow.PendingAsync(SignInStep.SetUp) is not { User: var user })
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        await ShowKeyAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await signInFlow.PendingAsync(SignInStep.SetUp) is not ({ } user, var persistent, var provider, var firstFactor))
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        if (!ModelState.IsValid || !throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            await ShowKeyAsync(user);
            return Page();
        }

        var confirmed = await twoFactor.ConfirmSetupAsync(user, Code, cancellationToken);
        if (confirmed.IsFailure)
        {
            ErrorMessage = localizer["That code is not right. Check it and try again."];
            await ShowKeyAsync(user);
            return Page();
        }

        await signInFlow.CompleteAsync(user, persistent, SignInFlow.MultiFactorMethod, provider, cancellationToken, firstFactor);
        RecoveryCodes = confirmed.Value.Codes;
        return Page();
    }

    private async Task ShowKeyAsync(Domain.Users.User user) =>
        Key = AuthenticatorKeyView.For((await twoFactor.PendingSetupAsync(user)).Value);
}
