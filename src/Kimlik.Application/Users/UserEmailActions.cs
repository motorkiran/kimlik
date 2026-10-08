using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Users;

/// <summary>Marks the address of a user as verified, for example after it was verified by another system.</summary>
public sealed class VerifyUserEmailHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await context.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (!user.EmailConfirmed)
        {
            user.MarkEmailVerified(timeProvider.GetUtcNow());
            auditLog.Record(AuditActions.UserEmailVerified, AuditSubject.User(userId));
            await context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>Sends the user a password reset link; also how a user created without a password sets one.</summary>
public sealed class SendPasswordResetHandler(IKimlikDbContext context, IOutbox outbox, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await context.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return UserErrors.NotFound;
        }

        outbox.Enqueue(new SendAccountEmail(userId, AccountEmail.PasswordReset));
        auditLog.Record(AuditActions.UserPasswordResetRequested, AuditSubject.User(userId));
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
