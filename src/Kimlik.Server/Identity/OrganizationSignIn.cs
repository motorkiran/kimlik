using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Identity;

/// <summary>Where a sign-in through an organization's own provider goes on: a local path, or why it cannot.</summary>
public sealed record OrganizationSignInResult(string? RedirectTo, string? ErrorMessage);

/// <summary>
/// Finishes a sign-in through an organization's own provider, over OpenID Connect or SAML: finds, links or creates the
/// account (see <see cref="SsoSignInHandler"/>), refuses accounts that cannot sign in, and goes on as after any other
/// first factor, or completes both factors when the provider verified several itself. A lockout after wrong passwords
/// does not stop it, as it does not stop a passkey: the provider signed the person in, and anyone could otherwise lock
/// them out of it.
/// </summary>
public sealed class OrganizationSignIn(
    SsoSignInHandler ssoSignIn,
    SignInFlow signInFlow,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IStringLocalizer<SharedResource> localizer)
{
    /// <summary>What people read when the provider's answer cannot be used.</summary>
    public string Failed => localizer["Signing in with your organization did not work. Try again."];

    public async Task<OrganizationSignInResult> ContinueAsync(
        SsoProvider connection, SsoIdentity identity, bool multiFactorAtProvider, string returnUrl, CancellationToken cancellationToken)
    {
        identity = identity with
        {
            GivenName = Limit(identity.GivenName, User.NameMaxLength),
            FamilyName = Limit(identity.FamilyName, User.NameMaxLength),
            Locale = Limit(identity.Locale, 16),
        };

        var signedIn = await ssoSignIn.HandleAsync(connection, identity, returnUrl, cancellationToken);
        if (signedIn.IsFailure)
        {
            return new OrganizationSignInResult(null, MessageFor(signedIn.Error));
        }

        var user = signedIn.Value;
        if (!user.CanSignIn)
        {
            return await RejectAsync(user, connection, "suspended", localizer["This account cannot sign in. Contact an administrator."], cancellationToken);
        }

        var next = await signInFlow.ContinueAsync(
            user, persistent: false, connection.LoginProvider, returnUrl, cancellationToken, multiFactorAtProvider: multiFactorAtProvider);
        return new OrganizationSignInResult(next, null);
    }

    private async Task<OrganizationSignInResult> RejectAsync(User user, SsoProvider connection, string reason, string message, CancellationToken cancellationToken)
    {
        auditLog.Record(
            AuditActions.UserSignInFailed,
            AuditSubject.User(user.Id),
            new Dictionary<string, object?> { ["reason"] = reason, ["provider"] = connection.LoginProvider },
            AuditActor.Anonymous);
        await context.SaveChangesAsync(cancellationToken);
        return new OrganizationSignInResult(null, message);
    }

    private string MessageFor(Error error) => error switch
    {
        _ when error == AccountErrors.AddressOutsideConnection =>
            localizer["Your organization's provider did not share an email address in its domains. Ask your administrator."],
        _ when error == AccountErrors.ProviderAlreadyLinked || error == AccountErrors.LoginInUse =>
            localizer["Your account is linked to another account at your organization's provider. Ask an administrator."],
        _ when error == AccountErrors.LookalikeAddress =>
            localizer["Another account has an address that reads the same as yours. Ask an administrator."],
        _ when error == AccountErrors.AddressNotVerifiedAtProvider =>
            localizer["Your organization's provider has not verified your email address. Ask your administrator."],
        _ => localizer["Something went wrong. Try again."],
    };

    private static string? Limit(string? value, int length) => value is { Length: > 0 } ? value[..Math.Min(value.Length, length)] : null;
}
