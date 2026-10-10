using Kimlik.Application.Accounts;
using Kimlik.Server.Identity;
using Kimlik.Server.SocialLogin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages;

/// <summary>
/// Kimlik's assertion consumer service, where SAML providers post their responses. A response counts only when it
/// answers the request this browser started (see <see cref="SamlServiceProvider"/>); the sign-in then goes on through
/// <see cref="OrganizationSignIn"/>, as a first factor.
/// </summary>
[IgnoreAntiforgeryToken] // Providers post from their own site; the request this browser started protects the response.
public sealed class SignInSamlModel(SamlServiceProvider saml, SsoDirectory directory, OrganizationSignIn organizationSignIn) : PageModel
{
    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet() => RedirectToPage("/SignIn");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (saml.TakePending(HttpContext) is not { } pending
            || await directory.FindAsync(pending.ConnectionId, cancellationToken) is not { Enabled: true, Protocol: Domain.Organizations.SsoProtocol.Saml } connection
            || saml.Read(Request.Form, connection, pending) is not { } identity)
        {
            return Fail(organizationSignIn.Failed);
        }

        var next = await organizationSignIn.ContinueAsync(connection, identity, multiFactorAtProvider: false, pending.ReturnUrl, cancellationToken);
        return next.RedirectTo is { } path ? LocalRedirect(path) : Fail(next.ErrorMessage!);
    }

    private PageResult Fail(string message)
    {
        ErrorMessage = message;
        return Page();
    }
}
