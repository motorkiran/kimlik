using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Accounts;
using Kimlik.Application.Branding;
using Kimlik.Application.Users;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Mfa;

/// <summary>
/// A user's two-factor authentication with an authenticator app and recovery codes. Setting up takes two steps: a new
/// key, then a code from the app that proves it was added. Turning it off or replacing the recovery codes takes a
/// current code. Every change updates the security stamp, which ends other sessions and trusted browsers.
/// </summary>
public sealed class TwoFactor(
    UserManager<User> userManager,
    MfaPolicy policy,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOptions<BrandingOptions> branding)
{
    public const int RecoveryCodeCount = 10;

    public async Task<Result<MfaStatusResponse>> StatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        return new MfaStatusResponse(
            user.TwoFactorEnabled,
            await policy.IsRequiredAsync(userId, cancellationToken),
            user.TwoFactorEnabled ? await userManager.CountRecoveryCodesAsync(user) : 0);
    }

    /// <summary>Gives the user a new authenticator key; two-factor authentication stays off until a code confirms it.</summary>
    public async Task<Result<AuthenticatorSetupResponse>> BeginSetupAsync(Guid userId)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (user.TwoFactorEnabled)
        {
            return MfaErrors.AlreadyEnabled;
        }

        await userManager.ResetAuthenticatorKeyAsync(user);
        return await DescribeKeyAsync(user);
    }

    /// <summary>The key being set up, so a page can show it again; a key is created if there is none.</summary>
    public async Task<Result<AuthenticatorSetupResponse>> PendingSetupAsync(User user)
    {
        if (await userManager.GetAuthenticatorKeyAsync(user) is null)
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
        }

        return await DescribeKeyAsync(user);
    }

    /// <summary>Turns two-factor authentication on with a code from the app, and returns the first recovery codes.</summary>
    public async Task<Result<RecoveryCodesResponse>> ConfirmSetupAsync(User user, string code, CancellationToken cancellationToken)
    {
        if (user.TwoFactorEnabled)
        {
            return MfaErrors.AlreadyEnabled;
        }

        if (await userManager.GetAuthenticatorKeyAsync(user) is null)
        {
            return MfaErrors.SetupNotStarted;
        }

        if (!await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
        {
            return MfaErrors.InvalidCode;
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);

        auditLog.Record(AuditActions.UserMfaEnabled, AuditSubject.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);

        return new RecoveryCodesResponse([.. codes!]);
    }

    public async Task<Result<RecoveryCodesResponse>> ConfirmSetupAsync(Guid userId, string code, CancellationToken cancellationToken) =>
        await userManager.FindByIdAsync(userId.ToString()) is { } user ? await ConfirmSetupAsync(user, code, cancellationToken) : UserErrors.NotFound;

    /// <summary>Replaces the recovery codes, which also invalidates the old ones.</summary>
    public async Task<Result<RecoveryCodesResponse>> RegenerateRecoveryCodesAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var user = await VerifiedUserAsync(userId, code);
        if (user.IsFailure)
        {
            return user.Error;
        }

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user.Value, RecoveryCodeCount);
        auditLog.Record(AuditActions.UserRecoveryCodesRegenerated, AuditSubject.User(userId));
        await context.SaveChangesAsync(cancellationToken);

        return new RecoveryCodesResponse([.. codes!]);
    }

    /// <summary>Turns two-factor authentication off, unless the policy requires it for the user.</summary>
    public async Task<Result> DisableAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        if (await policy.IsRequiredAsync(userId, cancellationToken))
        {
            return MfaErrors.Required;
        }

        var user = await VerifiedUserAsync(userId, code);
        if (user.IsFailure)
        {
            return user.Error;
        }

        await ClearAsync(user.Value);
        auditLog.Record(AuditActions.UserMfaDisabled, AuditSubject.User(userId));
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result<User>> VerifiedUserAsync(Guid userId, string code)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (!user.TwoFactorEnabled)
        {
            return MfaErrors.NotEnabled;
        }

        // Like at sign-in, wrong codes count toward the lockout, and a locked-out account accepts none.
        if (await userManager.IsLockedOutAsync(user))
        {
            return AccountErrors.LockedOut;
        }

        if (await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
        {
            await userManager.ResetAccessFailedCountAsync(user);
            return user;
        }

        await userManager.AccessFailedAsync(user);
        return await userManager.IsLockedOutAsync(user) ? AccountErrors.LockedOut : MfaErrors.InvalidCode;
    }

    /// <summary>
    /// Removes the second factor: the setting goes off, the key is replaced by one nobody has seen, and no recovery
    /// codes are left.
    /// </summary>
    internal async Task ClearAsync(User user)
    {
        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
    }

    private async Task<AuthenticatorSetupResponse> DescribeKeyAsync(User user)
    {
        var secret = (await userManager.GetAuthenticatorKeyAsync(user))!;
        var issuer = Uri.EscapeDataString(branding.Value.ProductName);
        var account = Uri.EscapeDataString(user.Email ?? user.Id.ToString());

        // The otpauth URI format of Google Authenticator, which every authenticator app reads.
        return new AuthenticatorSetupResponse(secret, $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}&digits=6&period=30");
    }
}

/// <summary>
/// Removes a user's second factor for someone who lost their device; they set it up again at their next sign-in if
/// the policy requires it. Their sessions end.
/// </summary>
public sealed class ResetUserMfaHandler(
    UserManager<User> userManager,
    TwoFactor twoFactor,
    IUserSessions sessions,
    AccessGuard guard,
    IKimlikDbContext context,
    IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var guardResult = await guard.EnsureCanManageUserAsync(userId, cancellationToken);
        if (guardResult.IsFailure)
        {
            return guardResult;
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await twoFactor.ClearAsync(user);
        await sessions.RevokeAllAsync(userId, cancellationToken);
        auditLog.Record(AuditActions.UserMfaReset, AuditSubject.User(userId));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success();
    }
}
