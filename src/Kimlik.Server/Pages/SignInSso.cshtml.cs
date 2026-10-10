using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Server.SocialLogin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages;

/// <summary>
/// Signing in through the organization's own identity provider: the SSO connection that covers the address's domain,
/// or the one the sign-in flow sends a person to who must sign in through it.
/// </summary>
public sealed class SignInSsoModel(SsoDirectory directory, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public SignInSsoInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? connection, CancellationToken cancellationToken) =>
        connection is { } id && await directory.FindAsync(id, cancellationToken) is { Enabled: true } found
            ? SingleSignOn.Challenge(HttpContext, found, email: null, ReturnUrl)
            : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (await directory.ForAddressAsync(Input.Email, cancellationToken) is not { } connection)
        {
            ErrorMessage = localizer["Single sign-on is not set up for that address. Sign in another way."];
            return Page();
        }

        return SingleSignOn.Challenge(HttpContext, connection, Input.Email, ReturnUrl);
    }
}

public sealed class SignInSsoInput
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Work email")]
    public string Email { get; set; } = string.Empty;
}
