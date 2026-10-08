using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Users;

public sealed class UpdateUserHandler(IKimlikDbContext context, AccessGuard guard, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<UserResponse>> HandleAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (await context.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var guardResult = await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);
        if (guardResult.IsFailure)
        {
            return guardResult.Error;
        }

        user.UpdateProfile(
            request.HasGivenName ? request.GivenName : user.GivenName,
            request.HasFamilyName ? request.FamilyName : user.FamilyName,
            request.HasLocale ? request.Locale : user.Locale,
            timeProvider.GetUtcNow());

        auditLog.Record(AuditActions.UserUpdated, AuditSubject.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);

        return await context.ToResponseAsync(user, cancellationToken);
    }
}
