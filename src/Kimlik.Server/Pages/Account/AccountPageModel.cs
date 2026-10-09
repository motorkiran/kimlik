using System.Security.Claims;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages.Account;

/// <summary>
/// A page of the signed-in user's own account. The pages in this folder require the sign-in session; an administrator
/// acting as the user sees them, but changes nothing.
/// </summary>
[NotWhileImpersonating(ChangesOnly = true)]
public abstract class AccountPageModel : PageModel
{
    /// <summary>The signed-in user, from Identity's user ID claim on the session.</summary>
    protected Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
