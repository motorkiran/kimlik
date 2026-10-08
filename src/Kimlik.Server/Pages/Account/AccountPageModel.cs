using System.Security.Claims;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages.Account;

/// <summary>A page of the signed-in user's own account. The pages in this folder require the sign-in session.</summary>
public abstract class AccountPageModel : PageModel
{
    /// <summary>The signed-in user, from Identity's user ID claim on the session.</summary>
    protected Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Re-issues this browser's session after a change that updated the security stamp, which ends the user's other
    /// sessions and would otherwise end this one too at its next check.
    /// </summary>
    protected async Task KeepThisSessionAsync(SignInManager<User> signInManager)
    {
        if (await signInManager.UserManager.FindByIdAsync(UserId.ToString()) is { } user)
        {
            await signInManager.RefreshSignInAsync(user);
        }
    }
}
