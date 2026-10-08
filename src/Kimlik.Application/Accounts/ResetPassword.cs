using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Application.Accounts;

public sealed record ResetPasswordCommand(Guid UserId, string Token, string NewPassword);

/// <summary>
/// Sets a new password with the token from the reset email. Every existing session and token of the user
/// is revoked, because a reset usually means the old password can no longer be trusted. When the account had no
/// password or its address was not verified yet, the reset is the first proof that the person owns the address, so
/// accounts at other providers linked before it are unlinked: they may belong to someone who signed up with the
/// address before its owner did (pre-account hijacking).
/// </summary>
public sealed class ResetPasswordHandler(
    UserManager<User> userManager,
    IUserSessions sessions,
    IKimlikDbContext context,
    IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is not { CanSignIn: true })
        {
            return AccountErrors.InvalidLink;
        }

        var ownerUnproven = !user.EmailConfirmed || !await userManager.HasPasswordAsync(user);

        var result = await userManager.ResetPasswordAsync(user, command.Token, command.NewPassword);
        if (!result.Succeeded)
        {
            return result.Errors.Any(error => error.Code == new IdentityErrorDescriber().InvalidToken().Code)
                ? AccountErrors.InvalidLink
                : AccountErrors.FromIdentity(result.Errors);
        }

        // Following the link sent to the address proves the person owns it.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        if (ownerUnproven)
        {
            foreach (var login in await userManager.GetLoginsAsync(user))
            {
                await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
                auditLog.Record(
                    AuditActions.UserLoginUnlinked,
                    AuditSubject.User(user.Id),
                    new Dictionary<string, object?> { ["provider"] = login.LoginProvider, ["reason"] = "password_reset" },
                    AuditActor.User(user.Id));
            }
        }

        await sessions.RevokeAllAsync(user.Id, cancellationToken);

        auditLog.Record(AuditActions.UserPasswordReset, AuditSubject.User(user.Id), actor: AuditActor.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
