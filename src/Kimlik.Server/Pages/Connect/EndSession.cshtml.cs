using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Pages.Connect;

/// <summary>
/// RP-initiated logout. Signing out without asking is only safe when the request proves which user it is
/// for (a valid <c>id_token_hint</c> for the current user); otherwise the user confirms, so another site
/// cannot sign people out.
/// </summary>
[IgnoreAntiforgeryToken]
public sealed class EndSessionModel(SignOutService signOut, UserManager<User> userManager, IAntiforgery antiforgery) : PageModel
{
    private const string ConfirmField = "confirm";

    public string? Error { get; private set; }

    public string? ErrorDescription { get; private set; }

    public IEnumerable<KeyValuePair<string, StringValues>> RequestParameters =>
        (Request.HasFormContentType ? (IEnumerable<KeyValuePair<string, StringValues>>)Request.Form : Request.Query)
            .Where(parameter => parameter.Key is not ConfirmField && parameter.Key != "__RequestVerificationToken");

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => EndSessionAsync(cancellationToken);

    public Task<IActionResult> OnPostAsync(CancellationToken cancellationToken) => EndSessionAsync(cancellationToken);

    private async Task<IActionResult> EndSessionAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.GetOpenIddictServerResponse() is { Error: { Length: > 0 } error } response)
        {
            Error = error;
            ErrorDescription = response.ErrorDescription;
            return Page();
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            return CompleteSignOut();
        }

        var hint = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var hintIsForCurrentUser = hint.Principal?.GetClaim(Claims.Subject) is { } subject && subject == userManager.GetUserId(User);

        var confirmed = Request.HasFormContentType && Request.Form.ContainsKey(ConfirmField);
        if (confirmed)
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }

        if (!hintIsForCurrentUser && !confirmed)
        {
            return Page();
        }

        await signOut.SignOutAsync(User, cancellationToken);
        return CompleteSignOut();
    }

    /// <summary>Lets OpenIddict redirect to the client's registered post-logout redirect URI, or home.</summary>
    private SignOutResult CompleteSignOut() =>
        SignOut(new AuthenticationProperties { RedirectUri = "/" }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
