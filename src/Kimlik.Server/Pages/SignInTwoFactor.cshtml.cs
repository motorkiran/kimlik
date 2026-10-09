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

/// <summary>
/// The second step of signing in: a code from the authenticator app or a recovery code, for people who set up an app,
/// or a passkey, for people who have one.
/// </summary>
[RunsScripts]
public sealed class SignInTwoFactorModel(
    SignInManager<User> signInManager,
    SignInFlow signInFlow,
    PasskeyCeremonies passkeys,
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

    /// <summary>The authenticator's answer to the passkey request, as the browser serializes it.</summary>
    [BindProperty]
    public string? Credential { get; set; }

    /// <summary>The protected state of the passkey request, as it went out with the options.</summary>
    [BindProperty]
    public string? State { get; set; }

    public bool HasAuthenticator { get; private set; }

    public bool HasPasskey { get; private set; }

    public bool CanTrustBrowser { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await signInFlow.PendingAsync(SignInStep.Verify) is not { } pending)
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        await DescribeAsync(pending.User, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await signInFlow.PendingAsync(SignInStep.Verify) is not ({ } user, var persistent, var provider, var firstFactor))
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        await DescribeAsync(user, cancellationToken);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            return TooManyAttempts();
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

            // Issued again, so that the session keeps how the user signed in first.
            await signInFlow.CompleteAsync(user, persistent, SignInFlow.MultiFactorMethod, provider, cancellationToken, firstFactor);
            var returnUrl = AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
            return LocalRedirect(provider is null ? await signInFlow.OfferPasskeyAsync(user, returnUrl) : returnUrl);
        }

        await RecordFailureAsync(user, UseRecoveryCode ? "recovery_code" : "authenticator", result.IsLockedOut, cancellationToken);
        ErrorMessage = result.IsLockedOut
            ? localizer["Too many failed attempts. Try again later."]
            : localizer["That code is not right. Check it and try again."];
        return Page();
    }

    /// <summary>The options for verifying with one of the user's passkeys, with the state to post back.</summary>
    public async Task<IActionResult> OnPostPasskeyOptionsAsync()
    {
        if (await signInFlow.PendingAsync(SignInStep.Verify) is not { } pending)
        {
            return BadRequest();
        }

        return Content((await passkeys.BeginAssertionAsync(HttpContext, pending.User)).ToJson(), "application/json");
    }

    /// <summary>Verifies the second step with one of the user's passkeys.</summary>
    public async Task<IActionResult> OnPostPasskeyAsync(CancellationToken cancellationToken)
    {
        // The code field of the form is not part of this step.
        ModelState.Clear();

        if (await signInFlow.PendingAsync(SignInStep.Verify) is not ({ } user, var persistent, var provider, var firstFactor))
        {
            return RedirectToPage("/SignIn", new { ReturnUrl });
        }

        await DescribeAsync(user, cancellationToken);
        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            return TooManyAttempts();
        }

        if (Credential is not { Length: > 0 } || State is not { Length: > 0 }
            || await passkeys.CompleteAssertionAsync(Credential, State, HttpContext) is not { } assertion
            || assertion.User.Id != user.Id)
        {
            await RecordFailureAsync(user, "passkey", lockedOut: false, cancellationToken);
            ErrorMessage = localizer["The passkey could not be verified. Try again."];
            return Page();
        }

        // The passkey's signature counter has moved on.
        await signInManager.UserManager.AddOrUpdatePasskeyAsync(user, assertion.Passkey);
        if (Input.TrustBrowser && CanTrustBrowser)
        {
            await signInManager.RememberTwoFactorClientAsync(user);
        }

        await signInFlow.CompleteAsync(
            user, persistent, SignInFlow.MultiFactorMethod, provider, cancellationToken, firstFactor, secondFactor: SignInFlow.PasskeyMethod);
        return LocalRedirect(AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }

    private async Task DescribeAsync(User user, CancellationToken cancellationToken)
    {
        HasAuthenticator = user.TwoFactorEnabled;
        HasPasskey = await signInFlow.HasPasskeyAsync(user);
        CanTrustBrowser = await policy.MayRememberBrowserAsync(user.Id, cancellationToken);
    }

    private PageResult TooManyAttempts()
    {
        Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ErrorMessage = localizer["Too many attempts. Wait a minute and try again."];
        return Page();
    }

    private async Task RecordFailureAsync(User user, string method, bool lockedOut, CancellationToken cancellationToken)
    {
        auditLog.Record(
            lockedOut ? AuditActions.UserLockedOut : AuditActions.UserMfaChallengeFailed,
            AuditSubject.User(user.Id),
            new Dictionary<string, object?> { ["method"] = method },
            AuditActor.Anonymous);
        await context.SaveChangesAsync(cancellationToken);
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
