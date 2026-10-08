using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Application.Users;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Accounts;

/// <summary>An account at another provider, such as Google, as the provider identifies it.</summary>
public sealed record ExternalLogin(string Provider, string ProviderKey, string ProviderDisplayName);

/// <summary>
/// The accounts at other providers that users sign in with. A user links at most one account per provider, and an
/// account at a provider belongs to one user.
/// </summary>
public sealed class ExternalLogins(UserManager<User> userManager, IKimlikDbContext context, IAuditLog auditLog)
{
    public async Task<Result<IReadOnlyList<UserLoginResponse>>> ListAsync(Guid userId)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        return (await userManager.GetLoginsAsync(user))
            .Select(login => new UserLoginResponse(login.LoginProvider, login.ProviderDisplayName ?? login.LoginProvider))
            .OrderBy(login => login.Provider, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<Result> LinkAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if ((await userManager.GetLoginsAsync(user)).SingleOrDefault(existing => existing.LoginProvider == login.Provider) is { } linked)
        {
            return linked.ProviderKey == login.ProviderKey ? Result.Success() : AccountErrors.ProviderAlreadyLinked;
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var added = await AddAsync(user, login);
        if (added.IsFailure)
        {
            return added;
        }

        auditLog.Record(AuditActions.UserLoginLinked, AuditSubject.User(userId), new Dictionary<string, object?> { ["provider"] = login.Provider });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Unlinks the user's account at <paramref name="provider"/>. With <paramref name="keepASignInMethod"/>, which
    /// self-service uses, the user must keep a password or another linked account.
    /// </summary>
    public async Task<Result> UnlinkAsync(Guid userId, string provider, bool keepASignInMethod, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var logins = await userManager.GetLoginsAsync(user);
        if (logins.SingleOrDefault(login => login.LoginProvider == provider) is not { } unlinked)
        {
            return AccountErrors.LoginNotFound;
        }

        if (keepASignInMethod && logins.Count == 1 && !await userManager.HasPasswordAsync(user))
        {
            return AccountErrors.LastSignInMethod;
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var removed = await userManager.RemoveLoginAsync(user, unlinked.LoginProvider, unlinked.ProviderKey);
        if (!removed.Succeeded)
        {
            return Error.Failure("account.unlink_failed", string.Join(", ", removed.Errors.Select(error => error.Code)));
        }

        auditLog.Record(AuditActions.UserLoginUnlinked, AuditSubject.User(userId), new Dictionary<string, object?> { ["provider"] = provider });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Links the login, unless it belongs to someone else, which a unique key also enforces against races.</summary>
    internal async Task<Result> AddAsync(User user, ExternalLogin login)
    {
        try
        {
            var added = await userManager.AddLoginAsync(user, new UserLoginInfo(login.Provider, login.ProviderKey, login.ProviderDisplayName));
            return added.Succeeded ? Result.Success() : AccountErrors.LoginInUse;
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return AccountErrors.LoginInUse;
        }
    }
}
