using Kimlik.Application.Accounts;
using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Kimlik.Server.SocialLogin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenIddict.Client.AspNetCore;

namespace Kimlik.Server.Pages.Account;

/// <summary>The accounts at other providers that the user signs in with: connected ones, and the rest to connect.</summary>
public sealed class LoginsModel(
    UserManager<User> userManager,
    SignInFlow signInFlow,
    ExternalProviders providers,
    ExternalLogins logins,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    public IReadOnlyList<ExternalProvider> Providers { get; private set; } = [];

    /// <summary>The names of the providers the user has connected an account at.</summary>
    public IReadOnlySet<string> Connected { get; private set; } = new HashSet<string>();

    public string? Notice { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await ShowAsync();

    public IActionResult OnPostConnect(string? provider)
    {
        if (providers.Find(provider) is null)
        {
            return RedirectToPage();
        }

        var properties = new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictClientAspNetCoreConstants.Properties.ProviderName] = provider,
            [ExternalProviders.LinkingUserProperty] = UserId.ToString(),
        })
        {
            RedirectUri = Url.Page("/Account/Logins"),
        };

        return Challenge(properties, OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
    }

    public async Task<IActionResult> OnPostDisconnectAsync(string? provider, CancellationToken cancellationToken)
    {
        var unlinked = await logins.UnlinkAsync(UserId, provider ?? string.Empty, keepASignInMethod: true, cancellationToken);
        if (unlinked.IsSuccess)
        {
            // Unlinking updates the security stamp.
            await signInFlow.RenewAsync();
            Notice = localizer["The account was disconnected."];
        }
        else if (unlinked.Error == AccountErrors.LastSignInMethod)
        {
            ErrorMessage = localizer["Set a password or connect another account first, so you can still sign in."];
        }

        return await ShowAsync();
    }

    private async Task<IActionResult> ShowAsync()
    {
        if (await userManager.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        Providers = providers.All;
        Connected = (await userManager.GetLoginsAsync(user)).Select(login => login.LoginProvider).ToHashSet(StringComparer.Ordinal);
        return Page();
    }
}
