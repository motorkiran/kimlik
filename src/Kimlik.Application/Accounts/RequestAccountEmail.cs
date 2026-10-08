using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Application.Accounts;

public sealed record ResendEmailVerificationCommand(string Email, string? ReturnUrl = null);

/// <summary>
/// Sends a new verification link. The caller always gets the same answer, so the form cannot be used to
/// find out which addresses have accounts.
/// </summary>
public sealed class ResendEmailVerificationHandler(UserManager<User> userManager, IKimlikDbContext context, IOutbox outbox)
{
    public async Task HandleAsync(ResendEmailVerificationCommand command, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(command.Email) is { EmailConfirmed: false, CanSignIn: true } user)
        {
            outbox.Enqueue(new SendAccountEmail(user.Id, AccountEmail.EmailVerification, command.ReturnUrl));
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed record RequestPasswordResetCommand(string Email);

/// <summary>Sends a password reset link; like resending verification, it never reveals whether the address is known.</summary>
public sealed class RequestPasswordResetHandler(UserManager<User> userManager, IKimlikDbContext context, IOutbox outbox, IAuditLog auditLog)
{
    public async Task HandleAsync(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(command.Email) is { CanSignIn: true } user)
        {
            outbox.Enqueue(new SendAccountEmail(user.Id, AccountEmail.PasswordReset));
            auditLog.Record(AuditActions.UserPasswordResetRequested, AuditSubject.User(user.Id));
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
