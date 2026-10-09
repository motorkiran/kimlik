using System.Security.Claims;
using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages;

/// <summary>
/// Adds a second factor to a session that started without one, where it is required, as by the admin panel or for
/// approving a device: the user verifies a code, or sets up an authenticator, and comes back. A trusted browser counts as
/// verified.
/// </summary>
[Authorize]
[NotWhileImpersonating]
public sealed class SignInStepUpModel(UserManager<User> userManager, SignInFlow signInFlow) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        returnUrl = AccountLinks.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        if (User.HasClaim(SignInFlow.MethodClaim, SignInFlow.MultiFactorMethod) || await userManager.GetUserAsync(User) is not { } user)
        {
            return LocalRedirect(returnUrl);
        }

        var persistent = (await HttpContext.AuthenticateAsync()).Properties?.IsPersistent == true;
        var provider = User.FindFirstValue(SignInFlow.ProviderClaim);
        var step = user.TwoFactorEnabled ? await signInFlow.NextStepAsync(user, cancellationToken) : SignInStep.SetUp;

        if (step == SignInStep.TrustedBrowser)
        {
            await signInFlow.CompleteAsync(user, persistent, SignInFlow.MultiFactorMethod, provider, cancellationToken, SignInFlow.FirstFactorOf(User));
            return LocalRedirect(returnUrl);
        }

        await signInFlow.DeferAsync(user, persistent, step, provider, SignInFlow.FirstFactorOf(User));
        return RedirectToPage(step == SignInStep.Verify ? "/SignInTwoFactor" : "/SignInSetUpTwoFactor", new { returnUrl });
    }
}
