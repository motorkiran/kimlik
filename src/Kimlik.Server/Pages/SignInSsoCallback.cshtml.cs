using System.Security.Claims;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using OpenIddict.Abstractions;
using OpenIddict.Client.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Pages;

/// <summary>
/// Where organizations' providers send people back. The provider vouches for the person, whose account it finds,
/// links or creates (see <see cref="SsoSignInHandler"/>); the sign-in goes on as after any other first factor, or
/// completes when the provider verified several factors itself.
/// </summary>
[IgnoreAntiforgeryToken] // Providers may post their response; OpenIddict's state token protects the request.
public sealed class SignInSsoCallbackModel(
    SsoDirectory directory,
    SsoSignInHandler ssoSignIn,
    UserManager<User> userManager,
    SignInFlow signInFlow,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IStringLocalizer<SharedResource> localizer) : PageModel
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
            return Fail(localizer["Signing in with your organization did not work. Try again."]);
        }

        var principal = result.Principal;
        var identity = new SsoIdentity(
            subject,
            principal.FindFirstValue(ClaimTypes.Email) ?? principal.GetClaim(Claims.Email),
            Limit(principal.FindFirstValue(ClaimTypes.GivenName) ?? principal.GetClaim(Claims.GivenName), Kimlik.Domain.Users.User.NameMaxLength),
            Limit(principal.FindFirstValue(ClaimTypes.Surname) ?? principal.GetClaim(Claims.FamilyName), Kimlik.Domain.Users.User.NameMaxLength),
            Limit(principal.GetClaim(Claims.Locale), 16),
            principal.GetClaim("hd"));
        var returnUrl = AccountLinks.IsLocalUrl(result.Properties.RedirectUri) ? result.Properties.RedirectUri! : "/";

        var signedIn = await ssoSignIn.HandleAsync(connection, identity, returnUrl, cancellationToken);
        if (signedIn.IsFailure)
        {
            return Fail(MessageFor(signedIn.Error));
        }

        var user = signedIn.Value;
        if (!user.CanSignIn)
        {
            return await RejectAsync(user, connection, "suspended", localizer["This account cannot sign in. Contact an administrator."], cancellationToken);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return await RejectAsync(user, connection, "locked_out", localizer["Too many failed attempts. Try again later."], cancellationToken);
        }

        // Providers report the factors they verified in the ID token, such as ["pwd", "mfa"] (RFC 8176).
        var multiFactor = principal.HasClaim(Claims.AuthenticationMethodReference, SignInFlow.MultiFactorMethod);
        return LocalRedirect(await signInFlow.ContinueAsync(
            user, persistent: false, connection.LoginProvider, returnUrl, cancellationToken, multiFactorAtProvider: multiFactor));
    }

    private async Task<IActionResult> RejectAsync(User user, SsoProvider connection, string reason, string message, CancellationToken cancellationToken)
    {
        auditLog.Record(
            AuditActions.UserSignInFailed,
            AuditSubject.User(user.Id),
            new Dictionary<string, object?> { ["reason"] = reason, ["provider"] = connection.LoginProvider },
            AuditActor.Anonymous);
        await context.SaveChangesAsync(cancellationToken);
        return Fail(message);
    }

    private string MessageFor(Error error) => error switch
    {
        _ when error == AccountErrors.AddressOutsideConnection =>
            localizer["Your organization's provider did not share an email address in its domains. Ask your administrator."],
        _ when error == AccountErrors.ProviderAlreadyLinked || error == AccountErrors.LoginInUse =>
            localizer["Your account is linked to another account at your organization's provider. Ask an administrator."],
        _ => localizer["Something went wrong. Try again."],
    };

    private static string? Limit(string? value, int length) => value is { Length: > 0 } ? value[..Math.Min(value.Length, length)] : null;

    private PageResult Fail(string message)
    {
        ErrorMessage = message;
        return Page();
    }
}
