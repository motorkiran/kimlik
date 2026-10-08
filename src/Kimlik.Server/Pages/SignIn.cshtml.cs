using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages;

public sealed class SignInModel(
    SignInManager<User> signInManager,
    SignInFlow signInFlow,
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

    public bool CanSignUp => accounts.Value.Registration == RegistrationMode.Open;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
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
