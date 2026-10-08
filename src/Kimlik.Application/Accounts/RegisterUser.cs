using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Accounts;

/// <summary>
/// A sign-up. <c>ReturnUrl</c> is where to continue after the email address is verified, typically a pending
/// authorization request.
/// </summary>
public sealed record RegisterUserCommand(string Email, string Password, string? GivenName, string? FamilyName, string? Locale, string? ReturnUrl = null);

/// <summary>Creates an account through self-service sign-up.</summary>
public sealed class RegisterUserHandler(
    UserManager<User> userManager,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOutbox outbox,
    DefaultUserRoles defaultRoles,
    IAccountEmailThrottle throttle,
    IOptions<AccountOptions> options,
    TimeProvider timeProvider)
{
    public async Task<Result<User>> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        if (options.Value.Registration != RegistrationMode.Open)
        {
            return AccountErrors.RegistrationClosed;
        }

        var user = User.Create(command.Email, command.GivenName, command.FamilyName, command.Locale, timeProvider.GetUtcNow());

        var error = await CreateAsync(user, command, cancellationToken);
        if (error is null)
        {
            return user;
        }

        if (error == AccountErrors.EmailAlreadyRegistered)
        {
            await NotifyExistingAccountAsync(command.Email, cancellationToken);
        }

        return error;
    }

    /// <summary>Creates the user, its audit event and its verification email in one transaction.</summary>
    private async Task<Error?> CreateAsync(User user, RegisterUserCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await userManager.CreateAsync(user, command.Password);
            if (!result.Succeeded)
            {
                return AccountErrors.FromIdentity(result.Errors);
            }
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Another sign-up with the same address won the race; the unique index caught it.
            return AccountErrors.EmailAlreadyRegistered;
        }

        auditLog.Record(AuditActions.UserCreated, AuditSubject.User(user.Id), actor: AuditActor.User(user.Id));
        await defaultRoles.AssignAsync(user.Id, cancellationToken);
        if (options.Value.RequireVerifiedEmail)
        {
            outbox.Enqueue(new SendAccountEmail(user.Id, AccountEmail.EmailVerification, command.ReturnUrl));
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    /// <summary>
    /// When sign-up does not reveal taken addresses, the owner of the address learns about the attempt by
    /// email instead, with a way to sign in or reset their password.
    /// </summary>
    private async Task NotifyExistingAccountAsync(string email, CancellationToken cancellationToken)
    {
        if (options.Value.RequireVerifiedEmail
            && await userManager.FindByEmailAsync(email) is { } existing
            && throttle.TryAcquire(existing.Id, AccountEmail.AlreadyRegistered))
        {
            outbox.Enqueue(new SendAccountEmail(existing.Id, AccountEmail.AlreadyRegistered));
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
