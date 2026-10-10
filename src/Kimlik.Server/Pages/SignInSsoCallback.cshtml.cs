using System.Security.Claims;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Organizations;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using OpenIddict.Client.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Pages;

/// <summary>
/// Where organizations' OpenID Connect providers send people back. The provider vouches for the person, and the sign-in
/// goes on through <see cref="OrganizationSignIn"/>, as multi-factor when the provider says it verified several factors.
/// </summary>
[IgnoreAntiforgeryToken] // Providers may post their response; OpenIddict's state token protects the request.
public sealed class SignInSsoCallbackModel(SsoDirectory directory, OrganizationSignIn organizationSignIn) : PageModel
{
    public string? ErrorMessage { get; private set; }

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => HandleAsync(cancellationToken);

    public Task<IActionResult> OnPostAsync(CancellationToken cancellationToken) => HandleAsync(cancellationToken);

    private async Task<IActionResult> HandleAsync(CancellationToken cancellationToken)
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded
            || SsoConnection.IdOf(result.Principal.GetClaim(Claims.Private.RegistrationId)) is not { } id
            || await directory.FindAsync(id, cancellationToken) is not { Enabled: true } connection
            || (result.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? result.Principal.GetClaim(Claims.Subject)) is not { Length: > 0 } subject)
        {
            return Fail(organizationSignIn.Failed);
        }

        var principal = result.Principal;
        var identity = new SsoIdentity(
            subject,
            principal.FindFirstValue(ClaimTypes.Email) ?? principal.GetClaim(Claims.Email),
            principal.FindFirstValue(ClaimTypes.GivenName) ?? principal.GetClaim(Claims.GivenName),
            principal.FindFirstValue(ClaimTypes.Surname) ?? principal.GetClaim(Claims.FamilyName),
            principal.GetClaim(Claims.Locale),
            principal.GetClaim("hd"));
        var returnUrl = AccountLinks.IsLocalUrl(result.Properties.RedirectUri) ? result.Properties.RedirectUri! : "/";

        // Providers report the factors they verified in the ID token, such as ["pwd", "mfa"] (RFC 8176).
        var multiFactor = principal.HasClaim(Claims.AuthenticationMethodReference, SignInFlow.MultiFactorMethod);
        var next = await organizationSignIn.ContinueAsync(connection, identity, multiFactor, returnUrl, cancellationToken);
        return next.RedirectTo is { } path ? LocalRedirect(path) : Fail(next.ErrorMessage!);
    }

    private PageResult Fail(string message)
    {
        ErrorMessage = message;
        return Page();
    }
}
