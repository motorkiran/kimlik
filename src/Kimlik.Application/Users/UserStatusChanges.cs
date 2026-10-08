using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Application.Users;

/// <summary>
/// Blocks a user: sign-in is refused, every token and authorization is revoked at once, and the new security
/// stamp ends open browser sessions at their next validation.
/// </summary>
public sealed class SuspendUserHandler(
    UserManager<User> userManager,
    IUserSessions sessions,
    IKimlikDbContext context,
    AccessGuard guard,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var guardResult = await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);
        if (guardResult.IsFailure)
        {
            return guardResult;
        }

        if (user.Status == UserStatus.Suspended)
        {
            return Result.Success();
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        user.Suspend(timeProvider.GetUtcNow());
        await userManager.UpdateSecurityStampAsync(user);
        await sessions.RevokeAllAsync(user.Id, cancellationToken);

        auditLog.Record(AuditActions.UserSuspended, AuditSubject.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class ReactivateUserHandler(
    UserManager<User> userManager,
    IKimlikDbContext context,
    AccessGuard guard,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var guardResult = await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);
        if (guardResult.IsFailure)
        {
            return guardResult;
        }

        if (user.Status == UserStatus.Active)
        {
            return Result.Success();
        }

        user.Reactivate(timeProvider.GetUtcNow());
        auditLog.Record(AuditActions.UserReactivated, AuditSubject.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
