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
/// For an administrator acting as a user (see <see cref="SignInFlow.ImpersonateAsync"/>): who they act as and for how
/// long, why something only the user may do was refused, and a way to stop, which signs them out to sign in as
/// themselves again.
/// </summary>
[Authorize]
public sealed class ImpersonationModel(UserManager<User> userManager, SignInFlow signInFlow, TimeProvider timeProvider) : PageModel
{
    public string? Email { get; private set; }

    public string? AdministratorEmail { get; private set; }

    public int MinutesLeft { get; private set; }

    /// <summary>Whether the administrator was sent here from something only the user may do.</summary>
    public bool Blocked { get; private set; }

    public async Task<IActionResult> OnGetAsync(bool blocked)
    {
        if (SignInFlow.ActorOf(User) is not { } administratorId)
        {
            return RedirectToPage("/Index");
        }

        Blocked = blocked;
        Email = User.FindFirstValue(ClaimTypes.Email);
        AdministratorEmail = (await userManager.FindByIdAsync(administratorId))?.Email;
        var endsAt = (await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Properties?.ExpiresUtc ?? timeProvider.GetUtcNow();
        MinutesLeft = Math.Max(1, (int)Math.Ceiling((endsAt - timeProvider.GetUtcNow()).TotalMinutes));
        return Page();
    }

    public async Task<IActionResult> OnPostStopAsync(CancellationToken cancellationToken)
    {
        if (SignInFlow.ActorOf(User) is null)
        {
            return RedirectToPage("/Index");
        }

        var userId = userManager.GetUserId(User);
        await signInFlow.EndImpersonationAsync(cancellationToken);
        return RedirectToPage("/SignIn", new { returnUrl = $"{Request.PathBase}/admin/users/{userId}" });
    }
}
