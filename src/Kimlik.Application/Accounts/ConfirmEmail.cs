using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Application.Accounts;

public sealed record ConfirmEmailCommand(Guid UserId, string Token);

/// <summary>Confirms an email address with the token from the verification email.</summary>
public sealed class ConfirmEmailHandler(UserManager<User> userManager, IKimlikDbContext context, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            return AccountErrors.InvalidLink;
        }

        if (user.EmailConfirmed)
        {
            return Result.Success();
        }

        var result = await userManager.ConfirmEmailAsync(user, command.Token);
        if (!result.Succeeded)
        {
            return AccountErrors.InvalidLink;
        }

        auditLog.Record(AuditActions.UserEmailVerified, AuditSubject.User(user.Id), actor: AuditActor.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
