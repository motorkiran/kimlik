using Kimlik.Application.Abstractions;
using Kimlik.Application.Interactions;
using Kimlik.Domain.Interactions;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Kimlik.Server.Interactions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages;

/// <summary>
/// Where the login app sends the browser once it decided a challenge. Only the browser the challenge was handed over
/// from can complete it, once: an accepted challenge starts the session with the methods the login app named, and a
/// rejected one sends the app an <c>access_denied</c> error.
/// </summary>
public sealed class LoginChallengeCompletionModel(
    LoginChallenges challenges,
    UserManager<User> userManager,
    SignInFlow signInFlow,
    IKimlikDbContext context,
    IDataProtectionProvider dataProtection,
    IOptions<ServerOptions> server,
    TimeProvider timeProvider,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string challenge, CancellationToken cancellationToken)
    {
        if (await challenges.FindAsync(challenge, cancellationToken) is not { Status: LoginChallengeStatus.Accepted or LoginChallengeStatus.Rejected } decided
            || timeProvider.GetUtcNow() >= decided.ExpiresAt
            || !decided.IsHeldBy(Request.Cookies[LoginApps.BrowserCookie(server.Value)]))
        {
            return Fail();
        }

        var rejected = decided.Status == LoginChallengeStatus.Rejected;
        decided.Complete();
        await context.SaveChangesAsync(cancellationToken);
        if (rejected)
        {
            return Redirect(LoginApps.Rejected(decided.ReturnUrl, decided.RejectionReason, dataProtection, timeProvider));
        }

        if (await userManager.FindByIdAsync(decided.UserId!.Value.ToString()) is not { CanSignIn: true } user)
        {
            return Fail();
        }

        // Addresses of an organization that signs in through its own provider go there, as after any first factor.
        var methods = decided.AcceptedMethods;
        var provider = methods.Contains("fed", StringComparer.Ordinal) ? $"login_app:{decided.DecidedBy}" : null;
        if (await signInFlow.SingleSignOnPathAsync(user, provider, decided.ReturnUrl, cancellationToken) is { } singleSignOn)
        {
            return LocalRedirect(singleSignOn);
        }

        var passkey = methods.Contains("pop", StringComparer.Ordinal);
        var first = methods.FirstOrDefault(method => method is "pwd" or "email" or "sms" or "fed") ?? SignInFlow.PasskeyMethod;
        if (passkey && first == SignInFlow.PasskeyMethod)
        {
            await signInFlow.CompleteAsync(user, persistent: false, SignInFlow.PasskeyMethod, provider, cancellationToken, loginApp: decided.DecidedBy);
        }
        else if (passkey || methods.Count >= 2)
        {
            await signInFlow.CompleteAsync(
                user, persistent: false, SignInFlow.MultiFactorMethod, provider, cancellationToken, first, passkey ? SignInFlow.PasskeyMethod : null, decided.DecidedBy);
        }
        else
        {
            await signInFlow.CompleteAsync(user, persistent: false, first, provider, cancellationToken, loginApp: decided.DecidedBy);
        }

        return LocalRedirect(decided.ReturnUrl);
    }

    private PageResult Fail()
    {
        ErrorMessage = localizer["This sign-in can no longer be completed. Start again from the application."];
        return Page();
    }
}
