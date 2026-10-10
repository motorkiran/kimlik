using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Kimlik.Server.Captcha;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages;

/// <summary>Signing in with an address and a password, a code sent to the address, or a passkey.</summary>
[RunsScripts]
[ShowsCaptcha]
public sealed class SignInModel(
    SignInManager<User> signInManager,
    SignInFlow signInFlow,
    PasskeyCeremonies passkeys,
    EmailSignIn emailSignIn,
    PhoneSignIn phoneSignIn,
    CaptchaVerifier captcha,
    PendingSignInCode pendingSignInCode,
    PasswordHashTiming passwordHashTiming,
    RequestThrottle throttle,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOptions<AccountOptions> accounts,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public SignInInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public bool ShowResendVerification { get; private set; }

    public bool CanUseEmailCode => emailSignIn.Enabled;

    public bool CanUsePhone => phoneSignIn.Enabled;

    /// <summary>The authenticator's answer to a passkey sign-in, as the browser serializes it.</summary>
    [BindProperty]
    public string? Credential { get; set; }

    /// <summary>The protected state of the passkey sign-in, as it went out with the options.</summary>
    [BindProperty]
    public string? State { get; set; }

    public bool CanSignUp => accounts.Value.Registration == RegistrationMode.Open;

    public void OnGet()
    {
    }

    /// <summary>Sends a sign-in code to the address, if an account can sign in with it, and asks for the code either way.</summary>
    public async Task<IActionResult> OnPostEmailCodeAsync(CancellationToken cancellationToken)
    {
        // Only the address matters here, not the password field.
        ModelState.Clear();
        if (!emailSignIn.Enabled)
        {
            return Page();
        }

        if (Input.Email is not { Length: > 0 } email || !new EmailAddressAttribute().IsValid(email))
        {
            ModelState.AddModelError("Input.Email", localizer["Enter a valid email address."]);
            return Page();
        }

        if (!await captcha.PassesAsync(CaptchaForm.SignInCode, HttpContext, cancellationToken))
        {
            ErrorMessage = localizer["Confirm that you are not a robot."];
            return Page();
        }

        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ErrorMessage = localizer["Too many attempts. Wait a minute and try again."];
            return Page();
        }

        await emailSignIn.RequestAsync(email.Trim(), cancellationToken);
        pendingSignInCode.Start(HttpContext, SignInCodeChannel.Email, email.Trim(), Input.RememberMe, ReturnUrl);
        return RedirectToPage("/SignInCode", new { ReturnUrl });
    }

    /// <summary>The options for signing in with a passkey, with the state to post back.</summary>
    public async Task<IActionResult> OnPostPasskeyOptionsAsync() =>
        Content((await passkeys.BeginAssertionAsync(HttpContext)).ToJson(), "application/json");

    /// <summary>
    /// Signs in with a passkey. It verifies the user on the device, so it is both factors at once; and since it cannot be
    /// guessed, a lockout after wrong passwords does not stop it.
    /// </summary>
    public async Task<IActionResult> OnPostPasskeyAsync(CancellationToken cancellationToken)
    {
        // The address and password fields of the form are not part of this sign-in.
        ModelState.Clear();

        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ErrorMessage = localizer["Too many attempts. Wait a minute and try again."];
            return Page();
        }

        if (Credential is not { Length: > 0 } || State is not { Length: > 0 }
            || await passkeys.CompleteAssertionAsync(Credential, State, HttpContext) is not { } assertion)
        {
            return await RejectAsync(null, "passkey_rejected", localizer["The passkey could not be verified. Try again."], cancellationToken);
        }

        var user = assertion.User;
        if (!user.CanSignIn || !await signInManager.CanSignInAsync(user))
        {
            return await RejectAsync(user, user.CanSignIn ? "email_not_verified" : "suspended", localizer["This account cannot sign in."], cancellationToken);
        }

        // The passkey's signature counter has moved on.
        await signInManager.UserManager.AddOrUpdatePasskeyAsync(user, assertion.Passkey);
        var returnUrl = AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
        await signInFlow.CompleteAsync(user, Input.RememberMe, SignInFlow.PasskeyMethod, provider: null, cancellationToken);
        return LocalRedirect(returnUrl);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await captcha.PassesAsync(CaptchaForm.SignIn, HttpContext, cancellationToken))
        {
            ErrorMessage = localizer["Confirm that you are not a robot."];
            return Page();
        }

        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ErrorMessage = localizer["Too many attempts. Wait a minute and try again."];
            return Page();
        }

        var userManager = signInManager.UserManager;
        var user = await userManager.FindByEmailAsync(Input.Email);

        // Accounts that cannot sign in answer like unknown ones, and take as long, so the answer reveals no account.
        // A locked account does not check the password either: a different answer for the right one would let the
        // guessing go on. Resetting the password lifts the lockout.
        if (user is null || !user.CanSignIn || await userManager.IsLockedOutAsync(user))
        {
            passwordHashTiming.VerifyAgainstPlaceholder(Input.Password);
            var reason = user is null ? "unknown_email" : user.CanSignIn ? "locked_out" : "suspended";
            return await RejectAsync(user, reason, InvalidCredentials, cancellationToken);
        }

        // Identity refuses an unverified address before it checks the password, which would tell anyone that the
        // account exists; checked here, only someone who knows the password learns why.
        if (!await signInManager.CanSignInAsync(user))
        {
            if (await userManager.CheckPasswordAsync(user, Input.Password))
            {
                ShowResendVerification = true;
                return await RejectAsync(user, "email_not_verified", localizer["Confirm your email address before signing in. Check your inbox for the verification link."], cancellationToken);
            }

            await userManager.AccessFailedAsync(user);
            return await RejectAsync(user, "invalid_password", InvalidCredentials, cancellationToken);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, Input.Password, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            var returnUrl = AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
            return LocalRedirect(await signInFlow.ContinueAsync(user, Input.RememberMe, provider: null, returnUrl, cancellationToken));
        }

        // The attempt that locks the account answers like any other wrong password.
        return await RejectAsync(user, result.IsLockedOut ? "locked_out" : "invalid_password", InvalidCredentials, cancellationToken);
    }

    private string InvalidCredentials => localizer["Invalid email or password."];

    private async Task<IActionResult> RejectAsync(User? user, string reason, string message, CancellationToken cancellationToken)
    {
        auditLog.Record(
            AuditActions.UserSignInFailed,
            user is null ? null : AuditSubject.User(user.Id),
            new Dictionary<string, object?> { ["reason"] = reason },
            AuditActor.Anonymous);
        await context.SaveChangesAsync(cancellationToken);

        ErrorMessage = message;
        return Page();
    }
}

public sealed class SignInInput
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [StringLength(AccountOptions.PasswordMaximumLength, ErrorMessage = "Use at most {1} characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}
