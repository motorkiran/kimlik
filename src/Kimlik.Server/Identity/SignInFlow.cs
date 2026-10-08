using System.Security.Claims;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Mfa;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Identity;

/// <summary>What happens after a correct password.</summary>
public enum SignInStep
{
    /// <summary>Signed in with the password alone.</summary>
    Password,

    /// <summary>The browser verified the second factor recently and the user chose to trust it.</summary>
    TrustedBrowser,

    /// <summary>A code from the authenticator app, or a recovery code, comes next.</summary>
    Verify,

    /// <summary>The policy requires a second factor that the user has not set up yet.</summary>
    SetUp,
}

/// <summary>
/// Signs users in once their password is right, with the second factor in between when they have one or must set
/// one up. Until then the sign-in waits in Identity's short-lived two-factor cookie, which Identity's own two-factor
/// sign-in reads, so its lockout counting applies to codes too.
/// </summary>
public sealed class SignInFlow(
    SignInManager<User> signInManager,
    MfaPolicy policy,
    IKimlikDbContext context,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    /// <summary>The authentication method claim Identity puts on sessions: <c>pwd</c>, or <c>mfa</c> after a second factor.</summary>
    public const string MethodClaim = "amr";
    public const string PasswordMethod = "pwd";
    public const string MultiFactorMethod = "mfa";

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

        return await policy.IsRequiredAsync(user.Id, cancellationToken) ? SignInStep.SetUp : SignInStep.Password;
    }

    /// <summary>Starts the session and records the sign-in.</summary>
    public async Task CompleteAsync(User user, bool persistent, string method, CancellationToken cancellationToken)
    {
        await signInManager.SignInWithClaimsAsync(user, persistent, [new Claim(MethodClaim, method)]);
        await signInManager.Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        await RecordAsync(user, method, cancellationToken);
    }

    /// <summary>Records a sign-in that Identity's two-factor sign-in completed.</summary>
    public async Task RecordAsync(User user, string method, CancellationToken cancellationToken)
    {
        await context.Users
            .Where(candidate => candidate.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.LastSignInAt, timeProvider.GetUtcNow()), cancellationToken);

        auditLog.Record(AuditActions.UserSignedIn, AuditSubject.User(user.Id), new Dictionary<string, object?> { ["method"] = method }, AuditActor.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Holds the sign-in until the second factor is verified or set up.</summary>
    public Task DeferAsync(User user, bool persistent, SignInStep step)
    {
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Id.ToString()));
        identity.AddClaim(new Claim(PersistentClaim, persistent ? "true" : "false"));
        identity.AddClaim(new Claim(StepClaim, step.ToString()));

        return signInManager.Context.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>The sign-in waiting for <paramref name="step"/>, if there is one.</summary>
    public async Task<(User User, bool Persistent)?> PendingAsync(SignInStep step)
    {
        var pending = await signInManager.Context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (pending.Principal?.FindFirstValue(StepClaim) != step.ToString()
            || await signInManager.UserManager.FindByIdAsync(pending.Principal.FindFirstValue(ClaimTypes.Name)!) is not { } user
            || !user.CanSignIn)
        {
            return null;
        }

        return (user, pending.Principal.FindFirstValue(PersistentClaim) == "true");
    }
}
