using Kimlik.Application.Abstractions;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages;

/// <summary>
/// Offered once, right after a password sign-in, to people without a passkey: add one now, or go on. Adding it runs
/// through the account pages' passkey handlers, which then continue to where the sign-in was going.
/// </summary>
[Authorize]
[RunsScripts]
public sealed class SignInPasskeyOfferModel(UserManager<User> userManager, IKimlikDbContext context, TimeProvider timeProvider) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string ContinueUrl => AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await userManager.GetUserAsync(User) is not { } user || user.PasskeyOfferedAt is not null)
        {
            return LocalRedirect(ContinueUrl);
        }

        user.MarkPasskeyOffered(timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        return Page();
    }
}
