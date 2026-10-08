using System.Security.Claims;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Mfa;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Identity;

/// <summary>What happens after the first factor: a correct password, or an account at another provider.</summary>
public enum SignInStep
{
    /// <summary>Signed in with the first factor alone.</summary>
    FirstFactor,

    /// <summary>The browser verified the second factor recently and the user chose to trust it.</summary>
    TrustedBrowser,

    /// <summary>A code from the authenticator app, or a recovery code, comes next.</summary>
    Verify,

    /// <summary>The policy requires a second factor that the user has not set up yet.</summary>
    SetUp,
}

/// <summary>
/// A sign-in waiting for its second factor. <c>Provider</c> names the provider of the account the user signed in
/// with, and is <see langword="null"/> after a password.
/// </summary>
public sealed record PendingSignIn(User User, bool Persistent, string? Provider);

/// <summary>
/// Signs users in once their first factor is right (a password, or an account at another provider), with the second
/// factor in between when they have one or must set one up. Until then the sign-in waits in Identity's short-lived
/// two-factor cookie, which Identity's own two-factor sign-in reads, so its lockout counting applies to codes too.
/// </summary>
public sealed class SignInFlow(
    SignInManager<User> signInManager,
    MfaPolicy policy,
    LinkGenerator links,
    IKimlikDbContext context,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    /// <summary>
    /// The authentication method claim Identity puts on sessions: <c>pwd</c> or <c>fed</c> for the first factor alone,
    /// or <c>mfa</c> after a second factor.
    /// </summary>
    public const string MethodClaim = "amr";
    public const string PasswordMethod = "pwd";
    public const string FederatedMethod = "fed";
    public const string MultiFactorMethod = "mfa";

    /// <summary>
    /// The claim naming the provider of a session that started at another provider. It is the one Identity's
    /// two-factor sign-in carries over from the pending sign-in to the session.
    /// </summary>
    public const string ProviderClaim = ClaimTypes.AuthenticationMethod;

    private const string PersistentClaim = "kimlik:persistent";
    private const string StepClaim = "kimlik:step";

    public async Task<SignInStep> NextStepAsync(User user, CancellationToken cancellationToken)
    {
        if (user.TwoFactorEnabled)
        {
            return await policy.MayRememberBrowserAsync(user.Id, cancellationToken) && await signInManager.IsTwoFactorClientRememberedAsync(user)
                ? SignInStep.TrustedBrowser
                : SignInStep.Verify;
        }

        return await policy.IsRequiredAsync(user.Id, cancellationToken) ? SignInStep.SetUp : SignInStep.FirstFactor;
    }

    /// <summary>
    /// Goes on after a correct first factor, a password or an account at <paramref name="provider"/>: starts the
    /// session, or holds it for the second factor. Returns where the browser goes next.
    /// </summary>
    public async Task<string> ContinueAsync(User user, bool persistent, string? provider, string returnUrl, CancellationToken cancellationToken)
    {
        var step = await NextStepAsync(user, cancellationToken);
        switch (step)
        {
            case SignInStep.FirstFactor:
                await CompleteAsync(user, persistent, provider is null ? PasswordMethod : FederatedMethod, provider, cancellationToken);
                return returnUrl;

            case SignInStep.TrustedBrowser:
                await CompleteAsync(user, persistent, MultiFactorMethod, provider, cancellationToken);
                return returnUrl;

            default:
                await DeferAsync(user, persistent, step, provider);
                var page = step == SignInStep.Verify ? "/SignInTwoFactor" : "/SignInSetUpTwoFactor";
                return links.GetPathByPage(signInManager.Context, page, values: new { returnUrl })!;
        }
    }

    /// <summary>Starts the session and records the sign-in.</summary>
    public async Task CompleteAsync(User user, bool persistent, string method, string? provider, CancellationToken cancellationToken)
    {
        List<Claim> claims = [new(MethodClaim, method)];
        if (provider is not null)
        {
            claims.Add(new Claim(ProviderClaim, provider));
        }

        await signInManager.SignInWithClaimsAsync(user, persistent, claims);
        await signInManager.Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        await RecordAsync(user, method, provider, cancellationToken);
    }

    /// <summary>Records a sign-in that Identity's two-factor sign-in completed.</summary>
    public async Task RecordAsync(User user, string method, string? provider, CancellationToken cancellationToken)
    {
        await context.Users
            .Where(candidate => candidate.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.LastSignInAt, timeProvider.GetUtcNow()), cancellationToken);

        var data = new Dictionary<string, object?> { ["method"] = method };
        if (provider is not null)
        {
            data["provider"] = provider;
        }

        auditLog.Record(AuditActions.UserSignedIn, AuditSubject.User(user.Id), data, AuditActor.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Holds the sign-in until the second factor is verified or set up.</summary>
    public Task DeferAsync(User user, bool persistent, SignInStep step, string? provider)
    {
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Id.ToString()));
        identity.AddClaim(new Claim(PersistentClaim, persistent ? "true" : "false"));
        identity.AddClaim(new Claim(StepClaim, step.ToString()));
        if (provider is not null)
        {
            identity.AddClaim(new Claim(ProviderClaim, provider));
        }

        return signInManager.Context.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>The sign-in waiting for <paramref name="step"/>, if there is one.</summary>
    public async Task<PendingSignIn?> PendingAsync(SignInStep step)
    {
        var pending = await signInManager.Context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (pending.Principal?.FindFirstValue(StepClaim) != step.ToString()
            || await signInManager.UserManager.FindByIdAsync(pending.Principal.FindFirstValue(ClaimTypes.Name)!) is not { } user
            || !user.CanSignIn)
        {
            return null;
        }

        return new PendingSignIn(user, pending.Principal.FindFirstValue(PersistentClaim) == "true", pending.Principal.FindFirstValue(ProviderClaim));
    }
}
