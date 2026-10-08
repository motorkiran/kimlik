using Kimlik.Application.Abstractions;
using Kimlik.Application.Users;
using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Application.Accounts;

/// <summary>What signed-in users do with their own account: the profile, the password, sessions and deletion.</summary>
public sealed class MyAccount(
    UserManager<User> userManager,
    IUserSessions sessions,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictApplicationManager applications,
    IOpenIddictTokenManager tokens,
    IKimlikDbContext context,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    /// <summary>Changes the profile with JSON Merge Patch semantics, like the Management API.</summary>
    public async Task<Result<UserResponse>> UpdateProfileAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        user.UpdateProfile(
            request.HasGivenName ? request.GivenName : user.GivenName,
            request.HasFamilyName ? request.FamilyName : user.FamilyName,
            request.HasLocale ? request.Locale : user.Locale,
            timeProvider.GetUtcNow());

        auditLog.Record(AuditActions.UserUpdated, AuditSubject.User(userId));
        await userManager.UpdateAsync(user);
        await context.SaveChangesAsync(cancellationToken);

        return await context.ToResponseAsync(user, cancellationToken);
    }

    /// <summary>Replaces the password. Every session and token ends, the caller's own included, as after a reset.</summary>
    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var confirmed = await ConfirmPasswordAsync(user, request.CurrentPassword);
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        var changed = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            return AccountErrors.FromIdentity(changed.Errors);
        }

        await sessions.RevokeAllAsync(userId, cancellationToken);
        auditLog.Record(AuditActions.UserPasswordChanged, AuditSubject.User(userId));
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>The applications that can get tokens for the user.</summary>
    public async Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = new List<SessionResponse>();
        await foreach (var authorization in authorizations.FindBySubjectAsync(userId.ToString(), cancellationToken))
        {
            if (!await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken))
            {
                continue;
            }

            var application = await authorizations.GetApplicationIdAsync(authorization, cancellationToken) is { } applicationId
                ? await applications.FindByIdAsync(applicationId, cancellationToken)
                : null;

            result.Add(new SessionResponse(
                Guid.Parse((await authorizations.GetIdAsync(authorization, cancellationToken))!),
                application is null ? null : await applications.GetClientIdAsync(application, cancellationToken),
                application is null ? null : await applications.GetDisplayNameAsync(application, cancellationToken),
                [.. await authorizations.GetScopesAsync(authorization, cancellationToken)],
                await authorizations.GetCreationDateAsync(authorization, cancellationToken)));
        }

        return [.. result.OrderByDescending(session => session.CreatedAt)];
    }

    /// <summary>Signs an application out: its tokens stop working and it has to ask the user again.</summary>
    public async Task<Result> RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (await authorizations.FindByIdAsync(sessionId.ToString(), cancellationToken) is not { } authorization
            || await authorizations.GetSubjectAsync(authorization, cancellationToken) != userId.ToString())
        {
            return AccountErrors.SessionNotFound;
        }

        await authorizations.TryRevokeAsync(authorization, cancellationToken);
        await tokens.RevokeByAuthorizationIdAsync(sessionId.ToString(), cancellationToken);
        auditLog.Record(AuditActions.UserSessionRevoked, AuditSubject.User(userId), new Dictionary<string, object?> { ["session"] = sessionId });
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Signs out everywhere: every application session and token ends, and browser sessions at their next check.</summary>
    public async Task<Result> RevokeAllSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        await userManager.UpdateSecurityStampAsync(user);
        await sessions.RevokeAllAsync(userId, cancellationToken);
        auditLog.Record(AuditActions.UserSessionRevoked, AuditSubject.User(userId), new Dictionary<string, object?> { ["session"] = "all" });
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Deletes the account and its personal data once the password confirms it.</summary>
    public async Task<Result> DeleteAsync(Guid userId, DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var confirmed = await ConfirmPasswordAsync(user, request.Password);
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await sessions.RevokeAllAsync(userId, cancellationToken);
        var deleted = await userManager.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            return Error.Failure("user.delete_failed", string.Join(", ", deleted.Errors.Select(error => error.Code)));
        }

        auditLog.Record(AuditActions.UserDeleted, AuditSubject.User(userId));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Checks the password the way signing in does: wrong guesses count toward the lockout, and a locked-out account
    /// accepts no password until the lockout ends.
    /// </summary>
    private async Task<Result> ConfirmPasswordAsync(User user, string password)
    {
        if (await userManager.IsLockedOutAsync(user))
        {
            return AccountErrors.LockedOut;
        }

        if (await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.ResetAccessFailedCountAsync(user);
            return Result.Success();
        }

        await userManager.AccessFailedAsync(user);
        return await userManager.IsLockedOutAsync(user) ? AccountErrors.LockedOut : AccountErrors.WrongPassword;
    }
}
