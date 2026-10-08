using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Kimlik.Server.SocialLogin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using OpenIddict.Client.AspNetCore;

namespace Kimlik.Server.Pages;

/// <summary>
/// Where providers send people back after they signed in there. A linked account signs in, and an unknown one becomes
/// a new account. Kimlik never links accounts on an email address alone: when the address belongs to an existing
/// account, or someone is already signed in, the link waits until the person signs in to the Kimlik account and
/// confirms it.
/// </summary>
[IgnoreAntiforgeryToken] // Apple posts its response; OpenIddict's state token protects the request.
public sealed class SignInExternalCallbackModel(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    SignInFlow signInFlow,
    ExternalProviders providers,
    ExternalLogins logins,
    PendingLinks pendingLinks,
    RegisterExternalUserHandler register,
    GitHubEmails gitHubEmails,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOptions<AccountOptions> accounts,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    public string? ErrorMessage { get; private set; }

    public bool ShowResendVerification { get; private set; }

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => HandleAsync(cancellationToken);

    public Task<IActionResult> OnPostAsync(CancellationToken cancellationToken) => HandleAsync(cancellationToken);

    private async Task<IActionResult> HandleAsync(CancellationToken cancellationToken)
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded || ExternalIdentity.From(result.Principal, providers) is not { } identity)
        {
            return Fail(localizer["Signing in with that account did not work. Try again."]);
        }

        if (identity.Provider.Name == ExternalProviders.GitHub
            && result.Properties.GetTokenValue(OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessToken) is { } accessToken
            && await gitHubEmails.FindVerifiedAsync(accessToken, cancellationToken) is { } gitHubEmail)
        {
            identity = identity with { Email = gitHubEmail, EmailVerified = identity.Provider.TrustEmail };
        }

        var returnUrl = AccountLinks.IsLocalUrl(result.Properties.RedirectUri) ? result.Properties.RedirectUri! : "/";
        var signedIn = await userManager.GetUserAsync(User);
        var linked = await userManager.FindByLoginAsync(identity.Provider.Name, identity.Key);

        if (result.Properties.Items.TryGetValue(ExternalProviders.LinkingUserProperty, out var linkingUser))
        {
            return await ConnectAsync(identity, linkingUser, signedIn, linked, cancellationToken);
        }

        if (linked is not null)
        {
            return await SignInAsync(linked, identity, returnUrl, cancellationToken);
        }

        if (signedIn is not null || (identity.Email is not null && await userManager.FindByEmailAsync(identity.Email) is not null))
        {
            return await OfferLinkAsync(identity, returnUrl);
        }

        if (identity.Email is null)
        {
            return Fail(localizer["Your {0} account does not share an email address, which an account here needs. Create an account with your email address instead.", identity.Provider.DisplayName]);
        }

        var created = await register.HandleAsync(
            new RegisterExternalUserCommand(identity.Login, identity.Email, identity.EmailVerified, identity.GivenName, identity.FamilyName, identity.Locale, returnUrl),
            cancellationToken);

        if (created.IsFailure)
        {
            return created.Error == AccountErrors.EmailAlreadyRegistered ? await OfferLinkAsync(identity, returnUrl) : Fail(MessageFor(created.Error));
        }

        if (!created.Value.EmailConfirmed && accounts.Value.RequireVerifiedEmail)
        {
            return RedirectToPage("/SignUpComplete");
        }

        return LocalRedirect(await signInFlow.ContinueAsync(created.Value, persistent: false, identity.Provider.Name, returnUrl, cancellationToken));
    }

    private async Task<IActionResult> SignInAsync(User user, ExternalIdentity identity, string returnUrl, CancellationToken cancellationToken)
    {
        if (!user.CanSignIn)
        {
            return await RejectAsync(user, identity, "suspended", localizer["This account cannot sign in. Contact an administrator."], cancellationToken);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return await RejectAsync(user, identity, "locked_out", localizer["Too many failed attempts. Try again later."], cancellationToken);
        }

        if (!await signInManager.CanSignInAsync(user))
        {
            ShowResendVerification = true;
            return await RejectAsync(
                user, identity, "email_not_verified", localizer["Confirm your email address before signing in. Check your inbox for the verification link."], cancellationToken);
        }

        return LocalRedirect(await signInFlow.ContinueAsync(user, persistent: false, identity.Provider.Name, returnUrl, cancellationToken));
    }

    /// <summary>Links the account from the account pages, for the user who asked to and is still signed in.</summary>
    private async Task<IActionResult> ConnectAsync(ExternalIdentity identity, string? linkingUser, User? signedIn, User? linked, CancellationToken cancellationToken)
    {
        if (signedIn is null || signedIn.Id.ToString() != linkingUser)
        {
            return RedirectToPage("/SignIn", new { returnUrl = Url.Page("/Account/Logins") });
        }

        if (linked is not null && linked.Id != signedIn.Id)
        {
            return Fail(localizer["That {0} account is already connected to another account.", identity.Provider.DisplayName]);
        }

        var result = await logins.LinkAsync(signedIn.Id, identity.Login, cancellationToken);
        return result.IsSuccess ? RedirectToPage("/Account/Logins") : Fail(MessageFor(result.Error));
    }

    private async Task<IActionResult> OfferLinkAsync(ExternalIdentity identity, string returnUrl)
    {
        await pendingLinks.HoldAsync(identity);
        return RedirectToPage("/SignInLink", new { returnUrl });
    }

    private async Task<IActionResult> RejectAsync(User user, ExternalIdentity identity, string reason, string message, CancellationToken cancellationToken)
    {
        auditLog.Record(
            AuditActions.UserSignInFailed,
            AuditSubject.User(user.Id),
            new Dictionary<string, object?> { ["reason"] = reason, ["provider"] = identity.Provider.Name },
            AuditActor.Anonymous);
        await context.SaveChangesAsync(cancellationToken);
        return Fail(message);
    }

    private string MessageFor(Error error) => error switch
    {
        _ when error == AccountErrors.RegistrationClosed => localizer["Registration is closed. Ask an administrator for an invitation."],
        _ when error == AccountErrors.ProviderAlreadyLinked => localizer["Another account at this provider is already connected. Disconnect it first."],
        _ when error == AccountErrors.LoginInUse => localizer["That account is already connected to another account."],
        _ => localizer["Something went wrong. Try again."],
    };

    private PageResult Fail(string message)
    {
        ErrorMessage = message;
        return Page();
    }
}
