using System.Globalization;
using System.Security.Claims;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
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

    /// <summary>A code from the authenticator app or a recovery code, or a passkey, comes next.</summary>
    Verify,

    /// <summary>The policy requires a second factor that the user has not set up yet.</summary>
    SetUp,
}

/// <summary>
/// A sign-in waiting for its second factor. <c>Provider</c> names the provider of the account the user signed in
/// with, and is <see langword="null"/> otherwise; <c>FirstFactor</c> is how they signed in first, such as <c>pwd</c>.
/// </summary>
public sealed record PendingSignIn(User User, bool Persistent, string? Provider, string FirstFactor);

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
    BackChannelLogout backChannelLogout,
    TimeProvider timeProvider)
{
    /// <summary>
    /// The authentication method claim Identity puts on sessions: <c>pwd</c>, <c>fed</c>, <c>email</c> (a code sent by
    /// email) or <c>sms</c> (a code sent by text message) for the first factor alone, or <c>mfa</c> after a second factor,
    /// with <c>email</c> or <c>sms</c> kept beside it. A
    /// passkey, which verifies the user on the device, counts as both factors: its sessions carry <c>pop</c> (proof of
    /// possession of a key) and <c>mfa</c>.
    /// </summary>
    public const string MethodClaim = "amr";
    public const string PasswordMethod = "pwd";
    public const string FederatedMethod = "fed";
    public const string EmailMethod = "email";
    public const string SmsMethod = "sms";
    public const string MultiFactorMethod = "mfa";
    public const string PasskeyMethod = "pop";

    /// <summary>
    /// The claim naming the provider of a session that started at another provider. It is the one Identity's
    /// two-factor sign-in carries over from the pending sign-in to the session.
    /// </summary>
    public const string ProviderClaim = ClaimTypes.AuthenticationMethod;

    /// <summary>
    /// When the user signed in, in Unix seconds. The session cookie's own issue time moves whenever the cookie is
    /// renewed, so it cannot tell how recent the sign-in is.
    /// </summary>
    public const string SignedInAtClaim = "kimlik:signed_in_at";

    /// <summary>
    /// The ID of the browser session, which ID tokens carry as <c>sid</c> and logout tokens name (OpenID Connect
    /// Back-Channel Logout); stamped when the session starts, and kept while it is renewed.
    /// </summary>
    public const string SessionIdClaim = "kimlik:session_id";

    /// <summary>The second factor of a session that used a passkey for it; sessions without it used a one-time code.</summary>
    public const string SecondFactorClaim = "kimlik:second_factor";

    /// <summary>The administrator who signed in as the session's user, in a session that impersonates them.</summary>
    public const string ActorClaim = "kimlik:actor";

    /// <summary>How long an administrator can act as a user before signing in again.</summary>
    public static readonly TimeSpan ImpersonationLifetime = TimeSpan.FromMinutes(30);

    private const string PersistentClaim = "kimlik:persistent";
    private const string StepClaim = "kimlik:step";
    private const string FirstFactorClaim = "kimlik:first_factor";

    /// <summary>
    /// What follows a correct first factor. An authenticator app always asks for its code; a passkey verifies the second
    /// step when one is required, by the policy or by <paramref name="secondStepRequired"/> (as for a step-up), so that its
    /// owner need not set up an app.
    /// </summary>
    public async Task<SignInStep> NextStepAsync(User user, CancellationToken cancellationToken, bool secondStepRequired = false)
    {
        var required = secondStepRequired || (!user.TwoFactorEnabled && await policy.IsRequiredAsync(user.Id, cancellationToken));
        if (user.TwoFactorEnabled || (required && await HasPasskeyAsync(user)))
        {
            return await policy.MayRememberBrowserAsync(user.Id, cancellationToken) && await signInManager.IsTwoFactorClientRememberedAsync(user)
                ? SignInStep.TrustedBrowser
                : SignInStep.Verify;
        }

        return required ? SignInStep.SetUp : SignInStep.FirstFactor;
    }

    public async Task<bool> HasPasskeyAsync(User user) => (await signInManager.UserManager.GetPasskeysAsync(user)).Count > 0;

    /// <summary>
    /// Goes on after a correct first factor, a password, a code sent by email or text message
    /// (<paramref name="firstFactor"/> <see cref="EmailMethod"/> or <see cref="SmsMethod"/>) or an account at
    /// <paramref name="provider"/>: starts the session, or holds it for the second factor. Returns where the browser goes next.
    /// </summary>
    public async Task<string> ContinueAsync(
        User user, bool persistent, string? provider, string returnUrl, CancellationToken cancellationToken, string? firstFactor = null)
    {
        var first = firstFactor ?? (provider is null ? PasswordMethod : FederatedMethod);
        var step = await NextStepAsync(user, cancellationToken);
        switch (step)
        {
            case SignInStep.FirstFactor:
                await CompleteAsync(user, persistent, first, provider, cancellationToken);
                return provider is null ? await OfferPasskeyAsync(user, returnUrl) : returnUrl;

            case SignInStep.TrustedBrowser:
                await CompleteAsync(user, persistent, MultiFactorMethod, provider, cancellationToken, first);
                return provider is null ? await OfferPasskeyAsync(user, returnUrl) : returnUrl;

            default:
                await DeferAsync(user, persistent, step, provider, first);
                var page = step == SignInStep.Verify ? "/SignInTwoFactor" : "/SignInSetUpTwoFactor";
                return links.GetPathByPage(signInManager.Context, page, values: new { returnUrl })!;
        }
    }

    /// <summary>
    /// Where to go after a password sign-in: once per account, to the offer to add a passkey, for people who have none;
    /// otherwise to <paramref name="returnUrl"/>.
    /// </summary>
    public async Task<string> OfferPasskeyAsync(User user, string returnUrl) =>
        user.PasskeyOfferedAt is null && (await signInManager.UserManager.GetPasskeysAsync(user)).Count == 0
            ? links.GetPathByPage(signInManager.Context, "/SignInPasskeyOffer", values: new { returnUrl })!
            : returnUrl;

    /// <summary>
    /// Starts the session and records the sign-in. After a second factor (<paramref name="method"/>
    /// <see cref="MultiFactorMethod"/>), <paramref name="firstFactor"/> says how the user signed in first, and
    /// <paramref name="secondFactor"/> is <see cref="PasskeyMethod"/> when a passkey verified the second step.
    /// </summary>
    public async Task CompleteAsync(
        User user,
        bool persistent,
        string method,
        string? provider,
        CancellationToken cancellationToken,
        string? firstFactor = null,
        string? secondFactor = null)
    {
        List<Claim> claims = (method, firstFactor) switch
        {
            (PasskeyMethod, _) => [new(MethodClaim, PasskeyMethod), new(MethodClaim, MultiFactorMethod)],
            (MultiFactorMethod, EmailMethod or SmsMethod) => [new(MethodClaim, firstFactor), new(MethodClaim, MultiFactorMethod)],
            _ => [new(MethodClaim, method)],
        };

        if (method == MultiFactorMethod && secondFactor == PasskeyMethod)
        {
            claims.Add(new Claim(SecondFactorClaim, PasskeyMethod));
        }

        // A step-up, or signing in again, continues the browser session the user already has, with its ID.
        var current = signInManager.Context.User;
        if (SessionIdOf(current) is { } sessionId && signInManager.UserManager.GetUserId(current) == user.Id.ToString())
        {
            claims.Add(new Claim(SessionIdClaim, sessionId));
        }

        if (provider is not null)
        {
            claims.Add(new Claim(ProviderClaim, provider));
        }

        await signInManager.SignInWithClaimsAsync(user, persistent, claims);
        await signInManager.Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        await RecordAsync(user, method, provider, firstFactor, secondFactor, cancellationToken);
    }

    /// <summary>
    /// Signs the browser in as <paramref name="user"/> on behalf of the administrator signed in now, for support: the
    /// session keeps how and when the administrator signed in, names them as its actor, lasts at most
    /// <see cref="ImpersonationLifetime"/> and is neither persistent nor renewed.
    /// </summary>
    public async Task ImpersonateAsync(User user, ClaimsPrincipal administrator, CancellationToken cancellationToken)
    {
        var administratorId = signInManager.UserManager.GetUserId(administrator)!;
        // The impersonation gets a session ID of its own: ending it does not end the administrator's apps.
        var claims = KeptClaims(administrator).Where(claim => claim.Type != SessionIdClaim).Append(new Claim(ActorClaim, administratorId));
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = false,
            ExpiresUtc = timeProvider.GetUtcNow() + ImpersonationLifetime,
        };

        await signInManager.SignInWithClaimsAsync(user, properties, claims);
        auditLog.Record(
            AuditActions.UserImpersonationStarted,
            AuditSubject.User(user.Id),
            new Dictionary<string, object?> { ["administrator"] = administratorId },
            AuditActor.User(Guid.Parse(administratorId)));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Ends a session that impersonates its user, so the administrator signs in as themselves again.</summary>
    public async Task EndImpersonationAsync(CancellationToken cancellationToken)
    {
        var session = signInManager.Context.User;
        if (ActorOf(session) is { } administratorId && signInManager.UserManager.GetUserId(session) is { } userId)
        {
            auditLog.Record(
                AuditActions.UserImpersonationEnded,
                AuditSubject.User(Guid.Parse(userId)),
                new Dictionary<string, object?> { ["administrator"] = administratorId },
                AuditActor.User(Guid.Parse(administratorId)));

            // The apps the administrator signed in to as the user hear that the impersonation ended.
            if (SessionIdOf(session) is { } sessionId)
            {
                await backChannelLogout.EndSessionAsync(Guid.Parse(userId), sessionId, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        await signInManager.SignOutAsync();
    }

    /// <summary>The ID of the browser session; sessions from before it was stamped have none.</summary>
    public static string? SessionIdOf(ClaimsPrincipal session) => session.FindFirstValue(SessionIdClaim);

    /// <summary>The administrator acting as the session's user, when the session impersonates them.</summary>
    public static string? ActorOf(ClaimsPrincipal session) => session.FindFirstValue(ActorClaim);

    /// <summary>
    /// How the session's user signed in first: with a passkey (<c>pop</c>), a code sent by email or text message, an account
    /// at another provider or a password.
    /// </summary>
    public static string FirstFactorOf(ClaimsPrincipal session) =>
        session.HasClaim(MethodClaim, PasskeyMethod) ? PasskeyMethod
        : session.HasClaim(MethodClaim, EmailMethod) ? EmailMethod
        : session.HasClaim(MethodClaim, SmsMethod) ? SmsMethod
        : session.HasClaim(claim => claim.Type == ProviderClaim) ? FederatedMethod
        : PasswordMethod;

    /// <summary>
    /// Issues the session again for the same sign-in, after a change that updated the security stamp, which ends the
    /// user's other sessions and would otherwise end this one too at its next check. Unlike Identity's refresh, it keeps
    /// everything about how and when the user signed in.
    /// </summary>
    public async Task RenewAsync()
    {
        var session = await signInManager.Context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (session.Principal is null || await signInManager.UserManager.GetUserAsync(session.Principal) is not { } user)
        {
            return;
        }

        var kept = KeptClaims(session.Principal);
        await signInManager.SignInWithClaimsAsync(user, session.Properties, kept);
    }

    /// <summary>
    /// The claims about how, when and by whom a session was signed in, which a renewal keeps; the others come from the
    /// user again.
    /// </summary>
    public static IEnumerable<Claim> KeptClaims(ClaimsPrincipal session) => session.Claims
        .Where(claim => claim.Type is MethodClaim or ProviderClaim or SignedInAtClaim or SecondFactorClaim or ActorClaim or SessionIdClaim)
        .Select(claim => new Claim(claim.Type, claim.Value, claim.ValueType));

    /// <summary>How the session's user verified the second step, if they did: with a passkey (<c>pop</c>) or a one-time code.</summary>
    public static string? SecondFactorOf(ClaimsPrincipal session) =>
        !session.HasClaim(MethodClaim, MultiFactorMethod) || session.HasClaim(MethodClaim, PasskeyMethod) ? null
        : session.HasClaim(SecondFactorClaim, PasskeyMethod) ? PasskeyMethod
        : "otp";

    /// <summary>
    /// How recent a sign-in must be for what a stolen session must not do, such as adding a passkey or exporting the
    /// account's data.
    /// </summary>
    public static readonly TimeSpan RecentSignIn = TimeSpan.FromMinutes(10);

    public static bool SignedInRecently(AuthenticateResult session, DateTimeOffset now) =>
        SignedInAt(session) is { } signedInAt && now - signedInAt <= RecentSignIn;

    /// <summary>When the session's user signed in; sessions from before the claim fall back to the cookie's issue time.</summary>
    public static DateTimeOffset? SignedInAt(AuthenticateResult session) =>
        long.TryParse(session.Principal?.FindFirstValue(SignedInAtClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : session.Properties?.IssuedUtc;

    /// <summary>Holds the sign-in until the second factor is verified or set up.</summary>
    public Task DeferAsync(User user, bool persistent, SignInStep step, string? provider, string firstFactor)
    {
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Id.ToString()));
        identity.AddClaim(new Claim(PersistentClaim, persistent ? "true" : "false"));
        identity.AddClaim(new Claim(StepClaim, step.ToString()));
        identity.AddClaim(new Claim(FirstFactorClaim, firstFactor));
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

        var provider = pending.Principal.FindFirstValue(ProviderClaim);
        var firstFactor = pending.Principal.FindFirstValue(FirstFactorClaim) ?? (provider is null ? PasswordMethod : FederatedMethod);
        return new PendingSignIn(user, pending.Principal.FindFirstValue(PersistentClaim) == "true", provider, firstFactor);
    }

    /// <summary>Records a sign-in, with the first factor when a second one followed it.</summary>
    private async Task RecordAsync(
        User user, string method, string? provider, string? firstFactor, string? secondFactor, CancellationToken cancellationToken)
    {
        await context.Users
            .Where(candidate => candidate.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.LastSignInAt, timeProvider.GetUtcNow()), cancellationToken);

        var data = new Dictionary<string, object?> { ["method"] = method };
        if (firstFactor is not null && firstFactor != method)
        {
            data["first_factor"] = firstFactor;
        }

        if (secondFactor is not null)
        {
            data["second_factor"] = secondFactor;
        }

        if (provider is not null)
        {
            data["provider"] = provider;
        }

        auditLog.Record(AuditActions.UserSignedIn, AuditSubject.User(user.Id), data, AuditActor.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);
    }
}
