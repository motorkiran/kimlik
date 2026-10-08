using Kimlik.Application.Accounts;
using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Kimlik.Server.SocialLogin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages;

/// <summary>
/// Links an account at a provider that matched no linked login: the person signs in to their Kimlik account first,
/// which proves they own it, then confirms.
/// </summary>
public sealed class SignInLinkModel(
    UserManager<User> userManager,
    PendingLinks pendingLinks,
    ExternalLogins logins,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public PendingLink? Link { get; private set; }

    /// <summary>The address of the account the person is signed in to, if they are.</summary>
    public string? SignedInAs { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string ContinueUrl => AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";

    /// <summary>Signs in, then comes back here to confirm.</summary>
    public string SignInUrl => $"{Url.Page("/SignIn")}?returnUrl={Uri.EscapeDataString($"{Url.Page("/SignInLink")}?returnUrl={Uri.EscapeDataString(ContinueUrl)}")}";

    public async Task<IActionResult> OnGetAsync()
    {
        if (await ShowAsync() is not { } link)
        {
            return RedirectToPage("/SignIn", new { returnUrl = ContinueUrl });
        }

        Link = link;
        return Page();
    }

    public async Task<IActionResult> OnPostConnectAsync(CancellationToken cancellationToken)
    {
        if (await ShowAsync() is not { } link || await userManager.GetUserAsync(User) is not { } user)
        {
            return RedirectToPage();
        }

        Link = link;
        var linked = await logins.LinkAsync(user.Id, link.Login, cancellationToken);
        if (linked.IsFailure)
        {
            ErrorMessage = linked.Error == AccountErrors.ProviderAlreadyLinked
                ? localizer["Another account at this provider is already connected. Disconnect it first."]
                : linked.Error == AccountErrors.LoginInUse
                    ? localizer["That account is already connected to another account."]
                    : localizer["Something went wrong. Try again."];
            return Page();
        }

        await pendingLinks.ClearAsync();
        return LocalRedirect(ContinueUrl);
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        await pendingLinks.ClearAsync();
        return User.Identity?.IsAuthenticated == true ? LocalRedirect(ContinueUrl) : RedirectToPage("/SignIn", new { returnUrl = ContinueUrl });
    }

    private async Task<PendingLink?> ShowAsync()
    {
        SignedInAs = (await userManager.GetUserAsync(User))?.Email;
        return await pendingLinks.FindAsync();
    }
}
