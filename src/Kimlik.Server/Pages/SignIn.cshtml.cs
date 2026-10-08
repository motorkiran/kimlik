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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages;

public sealed class SignInModel(
    SignInManager<User> signInManager,
    PasswordHashTiming passwordHashTiming,
    RequestThrottle throttle,
    IKimlikDbContext context,
    IAuditLog auditLog,
    TimeProvider timeProvider,
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

        var user = await signInManager.UserManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            passwordHashTiming.VerifyAgainstPlaceholder(Input.Password);
            return await RejectAsync(null, "unknown_email", localizer["Invalid email or password."], cancellationToken);
        }

        if (!user.CanSignIn)
        {
            return await RejectAsync(user, "suspended", localizer["Invalid email or password."], cancellationToken);
        }

        var result = await signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await context.Users
                .Where(candidate => candidate.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.LastSignInAt, timeProvider.GetUtcNow()), cancellationToken);

            auditLog.Record(AuditActions.UserSignedIn, AuditSubject.User(user.Id), actor: AuditActor.User(user.Id));
            await context.SaveChangesAsync(cancellationToken);

            return LocalRedirect(AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
        }

        if (result.IsLockedOut)
        {
            return await RejectAsync(user, "locked_out", localizer["Too many failed attempts. Try again later."], cancellationToken);
        }

        if (result.IsNotAllowed)
        {
            // The password was right, so saying why is not an information leak.
            ShowResendVerification = true;
            return await RejectAsync(user, "email_not_verified", localizer["Confirm your email address before signing in. Check your inbox for the verification link."], cancellationToken);
        }

        return await RejectAsync(user, "invalid_password", localizer["Invalid email or password."], cancellationToken);
    }

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
