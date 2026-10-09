using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Application.Users;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Accounts;

/// <summary>What signed-in users do with their own account: the profile, the password and deletion.</summary>
public sealed class MyAccount(
    UserManager<User> userManager,
    IUserSessions sessions,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOptions<AccountOptions> options,
    TimeProvider timeProvider)
{
    public async Task<Result<ProfileResponse>> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        await userManager.FindByIdAsync(userId.ToString()) is { } user ? await context.ToProfileAsync(user, cancellationToken) : UserErrors.NotFound;

    /// <summary>Changes the profile with JSON Merge Patch semantics, like the Management API.</summary>
    public async Task<Result<ProfileResponse>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var pictureUrl = request.HasPictureUrl ? request.PictureUrl : user.PictureUrl;
        var timeZone = request.HasTimeZone ? request.TimeZone : user.TimeZone;
        if (ProfileFields.Check(pictureUrl, timeZone) is { } invalid)
        {
            return invalid;
        }

        user.UpdateProfile(
            request.HasGivenName ? request.GivenName : user.GivenName,
            request.HasFamilyName ? request.FamilyName : user.FamilyName,
            request.HasLocale ? request.Locale : user.Locale,
            pictureUrl,
            timeZone,
            timeProvider.GetUtcNow());

        auditLog.Record(AuditActions.UserUpdated, AuditSubject.User(userId));
        await userManager.UpdateAsync(user);
        await context.SaveChangesAsync(cancellationToken);

        return await context.ToProfileAsync(user, cancellationToken);
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

    /// <summary>
    /// Removes the password, confirmed with it; the person signs in with codes sent by email from then on. As after a
    /// change, every session and token ends, the caller's own included.
    /// </summary>
    public async Task<Result> RemovePasswordAsync(Guid userId, string currentPassword, CancellationToken cancellationToken)
    {
        if (!options.Value.EmailSignIn)
        {
            return AccountErrors.EmailSignInOff;
        }

        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var confirmed = await ConfirmPasswordAsync(user, currentPassword);
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        var removed = await userManager.RemovePasswordAsync(user);
        if (!removed.Succeeded)
        {
            return AccountErrors.FromIdentity(removed.Errors);
        }

        await sessions.RevokeAllAsync(userId, cancellationToken);
        auditLog.Record(AuditActions.UserPasswordRemoved, AuditSubject.User(userId));
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Gives a password to an account that signs in only with accounts at other providers.</summary>
    public async Task<Result> AddPasswordAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (await userManager.HasPasswordAsync(user))
        {
            return AccountErrors.PasswordAlreadySet;
        }

        var added = await userManager.AddPasswordAsync(user, password);
        if (!added.Succeeded)
        {
            return AccountErrors.FromIdentity(added.Errors);
        }

        auditLog.Record(AuditActions.UserPasswordChanged, AuditSubject.User(userId));
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
        return confirmed.IsFailure ? confirmed : await DeleteAsync(user, cancellationToken);
    }

    /// <summary>
    /// Deletes an account that has no password, which the hosted pages confirm another way. Accounts with a password
    /// confirm with it.
    /// </summary>
    public async Task<Result> DeleteWithoutPasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        return await userManager.HasPasswordAsync(user) ? AccountErrors.PasswordRequired : await DeleteAsync(user, cancellationToken);
    }

    private async Task<Result> DeleteAsync(User user, CancellationToken cancellationToken)
    {
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await sessions.RevokeAllAsync(user.Id, cancellationToken);
        var deleted = await userManager.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            return Error.Failure("user.delete_failed", string.Join(", ", deleted.Errors.Select(error => error.Code)));
        }

        auditLog.Record(AuditActions.UserDeleted, AuditSubject.User(user.Id));
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
