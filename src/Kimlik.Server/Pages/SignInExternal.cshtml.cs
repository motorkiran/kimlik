using Kimlik.Server.Identity;
using Kimlik.Server.SocialLogin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Client.AspNetCore;

namespace Kimlik.Server.Pages;

/// <summary>Sends the browser to a provider to sign in there; it comes back to <see cref="SignInExternalCallbackModel"/>.</summary>
public sealed class SignInExternalModel(ExternalProviders providers) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/SignIn");

    public IActionResult OnPost(string? provider, string? returnUrl)
    {
        if (providers.Find(provider) is null)
        {
            return RedirectToPage("/SignIn", new { returnUrl });
        }

        var properties = new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictClientAspNetCoreConstants.Properties.ProviderName] = provider,
        })
        {
            RedirectUri = AccountLinks.IsLocalUrl(returnUrl) ? returnUrl : "/",
        };

        return Challenge(properties, OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
    }
}
