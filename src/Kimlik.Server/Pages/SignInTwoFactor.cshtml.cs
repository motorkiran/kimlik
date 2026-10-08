using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Mfa;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages;

/// <summary>The second step of signing in: a code from the authenticator app, or a recovery code.</summary>
public sealed class SignInTwoFactorModel(
    SignInManager<User> signInManager,
    SignInFlow signInFlow,
    MfaPolicy policy,
    RequestThrottle throttle,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Whether the form asks for a recovery code instead of an authenticator code.</summary>
    [BindProperty(SupportsGet = true)]
    public bool UseRecoveryCode { get; set; }

    [BindProperty]
    public TwoFactorInput Input { get; set; } = new();

    public bool CanTrustBrowser { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await signInFlow.PendingAsync(SignInStep.Verify) is not { } pending)
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        CanTrustBrowser = await policy.MayRememberBrowserAsync(pending.User.Id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await signInFlow.PendingAsync(SignInStep.Verify) is not ({ } user, var persistent, var provider))
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        CanTrustBrowser = await policy.MayRememberBrowserAsync(user.Id, cancellationToken);
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

        var result = UseRecoveryCode
            ? await signInManager.TwoFactorRecoveryCodeSignInAsync(Input.Code)
            : await signInManager.TwoFactorAuthenticatorSignInAsync(Input.Code, persistent, rememberClient: Input.TrustBrowser && CanTrustBrowser);

        if (result.Succeeded)
        {
            if (UseRecoveryCode)
            {
                auditLog.Record(AuditActions.UserRecoveryCodeUsed, AuditSubject.User(user.Id), actor: AuditActor.User(user.Id));
            }

            await signInFlow.RecordAsync(user, SignInFlow.MultiFactorMethod, provider, cancellationToken);
            var returnUrl = AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
            return LocalRedirect(provider is null ? await signInFlow.OfferPasskeyAsync(user, returnUrl) : returnUrl);
        }

        auditLog.Record(
            result.IsLockedOut ? AuditActions.UserLockedOut : AuditActions.UserMfaChallengeFailed,
            AuditSubject.User(user.Id),
            new Dictionary<string, object?> { ["method"] = UseRecoveryCode ? "recovery_code" : "authenticator" },
            AuditActor.Anonymous);
        await context.SaveChangesAsync(cancellationToken);

        ErrorMessage = result.IsLockedOut
            ? localizer["Too many failed attempts. Try again later."]
            : localizer["That code is not right. Check it and try again."];
        return Page();
    }
}

public sealed class TwoFactorInput
{
    [Required(ErrorMessage = "Enter the code.")]
    [StringLength(32)]
    [Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;

    public bool TrustBrowser { get; set; }
}
