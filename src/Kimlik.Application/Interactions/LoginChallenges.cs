using System.Security.Cryptography;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Accounts;
using Kimlik.Application.Mfa;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Kimlik.Domain.Interactions;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Interactions;

/// <summary>The login app sign-ins are handed to, from the <c>Kimlik:Interactions</c> section (design §10.12).</summary>
public sealed class InteractionOptions
{
    public const string SectionName = "Kimlik:Interactions";

    /// <summary>
    /// The page of the login app, an absolute HTTP or HTTPS URL. Authorization requests that need a sign-in go there with
    /// a <c>login_challenge</c>, instead of to the hosted sign-in page; nothing is handed over while it is unset.
    /// </summary>
    public Uri? LoginUrl { get; set; }

    public bool IsValid() => LoginUrl is null || (LoginUrl.IsAbsoluteUri && (LoginUrl.Scheme == Uri.UriSchemeHttps || LoginUrl.Scheme == Uri.UriSchemeHttp));
}

/// <summary>An authorization request that needs a sign-in, as it is handed to the login app.</summary>
public sealed record LoginChallengeStart(string ClientId, string ReturnUrl, IEnumerable<string> Scopes, string? LoginHint, string? Prompt, RequestedStrength Strength);

/// <summary>Hands sign-ins to the login app, and finds the challenges it answers.</summary>
public sealed class LoginChallenges(IKimlikDbContext context, TimeProvider timeProvider)
{
    /// <summary>Starts a challenge for the browser holding <paramref name="browserSecret"/>, and returns the challenge to send it with.</summary>
    public async Task<string> StartAsync(LoginChallengeStart start, string browserSecret, CancellationToken cancellationToken)
    {
        var challenge = NewSecret();
        context.LoginChallenges.Add(LoginChallenge.Create(
            challenge, browserSecret, start.ClientId, start.ReturnUrl, start.Scopes, start.LoginHint, start.Prompt, start.Strength, timeProvider.GetUtcNow()));
        await context.SaveChangesAsync(cancellationToken);
        return challenge;
    }

    /// <summary>A random value that identifies a challenge, or a browser, and cannot be guessed.</summary>
    public static string NewSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>The challenge, if it exists and is still waiting for the login app.</summary>
    public async Task<LoginChallenge?> FindPendingAsync(string? challenge, CancellationToken cancellationToken)
    {
        var found = await FindAsync(challenge, cancellationToken);
        return found is not null && found.IsPending(timeProvider.GetUtcNow()) ? found : null;
    }

    public async Task<LoginChallenge?> FindAsync(string? challenge, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(challenge) || challenge.Length > 128)
        {
            return null;
        }

        var hash = LoginChallenge.Hash(challenge);
        return await context.LoginChallenges.SingleOrDefaultAsync(candidate => candidate.ChallengeHash == hash, cancellationToken);
    }

    /// <summary>
    /// Whether the user's sign-in must take a second factor: because they set one up, or a policy of Kimlik's asks for
    /// one, as on the hosted pages.
    /// </summary>
    internal static async Task<bool> SecondFactorRequiredAsync(User user, MfaPolicy policy, CancellationToken cancellationToken) =>
        user.TwoFactorEnabled || await policy.IsRequiredAsync(user.Id, cancellationToken);
}

public sealed class GetLoginChallengeHandler(IKimlikDbContext context, LoginChallenges challenges)
{
    public async Task<Result<LoginChallengeResponse>> HandleAsync(string challenge, CancellationToken cancellationToken)
    {
        if (await challenges.FindPendingAsync(challenge, cancellationToken) is not { } pending)
        {
            return InteractionErrors.ChallengeNotFound;
        }

        var clientName = await context.Applications.Where(application => application.ClientId == pending.ClientId)
            .Select(application => application.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);
        return new LoginChallengeResponse(
            pending.ClientId,
            clientName,
            [.. pending.Scopes],
            pending.LoginHint,
            pending.Prompt,
            pending.RequestedStrength != RequestedStrength.Any,
            pending.RequestedStrength == RequestedStrength.PhishingResistant,
            pending.ExpiresAt,
            $"/signin?ReturnUrl={Uri.EscapeDataString(pending.ReturnUrl)}");
    }
}

/// <summary>
/// Accepts a challenge for a user, signed in the way the login app says. Kimlik still holds the sign-in to its rules:
/// the user must be able to sign in, hold no system permission the login app does not, and the methods must make the
/// sign-in as strong as the request and Kimlik's policies need.
/// </summary>
public sealed class AcceptLoginChallengeHandler(
    IKimlikDbContext context,
    LoginChallenges challenges,
    UserManager<User> userManager,
    MfaPolicy policy,
    AccessGuard guard,
    IRequestContext request,
    IOptionsMonitor<AccountOptions> accounts,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(string challenge, AcceptLoginChallengeRequest accept, CancellationToken cancellationToken)
    {
        if (await challenges.FindPendingAsync(challenge, cancellationToken) is not { } pending)
        {
            return InteractionErrors.ChallengeNotFound;
        }

        if (await userManager.FindByIdAsync(accept.UserId.ToString()) is not { CanSignIn: true } user
            || (accounts.CurrentValue.RequireVerifiedEmail && !user.EmailConfirmed))
        {
            return InteractionErrors.UserCannotSignIn;
        }

        // Accepting signs the user in, so a login app signs in only users with no more access to Kimlik than its own;
        // administrators sign in on the hosted pages.
        var allowed = await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var secondFactorRequired = await LoginChallenges.SecondFactorRequiredAsync(user, policy, cancellationToken);
        var accepted = pending.Accept(user.Id, accept.Methods, secondFactorRequired, request.Actor.Id ?? "kimlik", timeProvider.GetUtcNow());
        if (accepted.IsFailure)
        {
            return accepted;
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class RejectLoginChallengeHandler(IKimlikDbContext context, LoginChallenges challenges, IRequestContext request, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(string challenge, RejectLoginChallengeRequest reject, CancellationToken cancellationToken)
    {
        if (await challenges.FindPendingAsync(challenge, cancellationToken) is not { } pending)
        {
            return InteractionErrors.ChallengeNotFound;
        }

        var rejected = pending.Reject(reject.Reason, request.Actor.Id ?? "kimlik", timeProvider.GetUtcNow());
        if (rejected.IsSuccess)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return rejected;
    }
}
