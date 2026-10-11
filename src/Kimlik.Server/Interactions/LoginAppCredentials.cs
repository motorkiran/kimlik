using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Application.Interactions;
using Kimlik.Application.Mfa;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Interactions;
using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Interactions;

/// <summary>
/// Checks credentials that Kimlik keeps for a login app, only for a pending challenge, which allows ten checks. They count
/// toward the lockout as on the hosted pages, and unknown addresses, wrong passwords and locked accounts get the same
/// answer, in the same time.
/// </summary>
public sealed class LoginAppCredentials(
    LoginChallenges challenges,
    UserManager<User> userManager,
    PasswordHashTiming passwordHashTiming,
    MfaPolicy policy,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOptionsMonitor<AccountOptions> accounts)
{
    public async Task<Result<VerifiedUserResponse>> VerifyPasswordAsync(string challenge, VerifyPasswordRequest request, CancellationToken cancellationToken)
    {
        if (await AttemptAsync(challenge, cancellationToken) is { } refused)
        {
            return refused;
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.CanSignIn || await userManager.IsLockedOutAsync(user))
        {
            passwordHashTiming.VerifyAgainstPlaceholder(request.Password);
            return await RejectAsync(user, user is null ? "unknown_email" : user.CanSignIn ? "locked_out" : "suspended", InteractionErrors.InvalidCredentials, cancellationToken);
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            return await RejectAsync(user, "invalid_password", InteractionErrors.InvalidCredentials, cancellationToken);
        }

        if (accounts.CurrentValue.RequireVerifiedEmail && !user.EmailConfirmed)
        {
            return await RejectAsync(user, "email_not_verified", InteractionErrors.UserCannotSignIn, cancellationToken);
        }

        await userManager.FirstFactorSucceededAsync(user);
        var secondFactorRequired = user.TwoFactorEnabled || await policy.IsRequiredAsync(user.Id, cancellationToken);
        return new VerifiedUserResponse(user.Id, secondFactorRequired, user.TwoFactorEnabled);
    }

    /// <summary>Checks a code from the user's authenticator app, which can be used once, or one of their recovery codes.</summary>
    public async Task<Result> VerifySecondFactorAsync(string challenge, VerifySecondFactorRequest request, CancellationToken cancellationToken)
    {
        if (await AttemptAsync(challenge, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await userManager.FindByIdAsync(request.UserId.ToString()) is not { CanSignIn: true, TwoFactorEnabled: true } user
            || await userManager.IsLockedOutAsync(user))
        {
            return InteractionErrors.WrongCode;
        }

        var code = request.Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
        {
            await userManager.ResetAccessFailedCountAsync(user);
            return Result.Success();
        }

        if ((await userManager.RedeemTwoFactorRecoveryCodeAsync(user, request.Code.Trim())).Succeeded)
        {
            auditLog.Record(AuditActions.UserRecoveryCodeUsed, AuditSubject.User(user.Id), actor: AuditActor.User(user.Id));
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        await userManager.AccessFailedAsync(user);
        return await RejectAsync(user, "wrong_code", InteractionErrors.WrongCode, cancellationToken);
    }

    /// <summary>Counts a check against the challenge; an error when it is not pending or has had its share.</summary>
    private async Task<Error?> AttemptAsync(string challenge, CancellationToken cancellationToken)
    {
        if (await challenges.FindPendingAsync(challenge, cancellationToken) is not { } pending)
        {
            return InteractionErrors.ChallengeNotFound;
        }

        return await challenges.TryAttemptAsync(pending, cancellationToken) ? null : InteractionErrors.TooManyAttempts;
    }

    private async Task<Error> RejectAsync(User? user, string reason, Error error, CancellationToken cancellationToken)
    {
        auditLog.Record(
            AuditActions.UserSignInFailed,
            user is null ? null : AuditSubject.User(user.Id),
            new Dictionary<string, object?> { ["reason"] = reason, ["login_app"] = true });
        await context.SaveChangesAsync(cancellationToken);
        return error;
    }
}
