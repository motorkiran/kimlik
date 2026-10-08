using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Application.Users;

/// <summary>
/// Deletes a user and their personal data. Role assignments and linked logins go with the account; the audit
/// trail keeps only the user's ID.
/// </summary>
public sealed class DeleteUserHandler(
    UserManager<User> userManager,
    IUserSessions sessions,
    IKimlikDbContext context,
    AccessGuard guard,
    IAuditLog auditLog)
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

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        await sessions.RevokeAllAsync(user.Id, cancellationToken);
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return Error.Failure("user.delete_failed", string.Join(", ", result.Errors.Select(error => error.Code)));
        }

        auditLog.Record(AuditActions.UserDeleted, AuditSubject.User(userId));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success();
    }
}
