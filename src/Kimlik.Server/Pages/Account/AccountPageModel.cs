using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages.Account;

/// <summary>A page of the signed-in user's own account. The pages in this folder require the sign-in session.</summary>
public abstract class AccountPageModel : PageModel
{
    /// <summary>The signed-in user, from Identity's user ID claim on the session.</summary>
    protected Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
