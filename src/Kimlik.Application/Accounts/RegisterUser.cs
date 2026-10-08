using System.Data.Common;
using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Accounts;

public sealed record RegisterUserCommand(string Email, string Password, string? GivenName, string? FamilyName, string? Locale);

/// <summary>Creates an account through self-service sign-up.</summary>
public sealed class RegisterUserHandler(
    UserManager<User> userManager,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOptions<AccountOptions> options,
    TimeProvider timeProvider)
{
    private const string UniqueViolationSqlState = "23505";

    public async Task<Result<User>> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        if (options.Value.Registration != RegistrationMode.Open)
        {
            return AccountErrors.RegistrationClosed;
        }

        var user = User.Create(command.Email, command.GivenName, command.FamilyName, command.Locale, timeProvider.GetUtcNow());

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await userManager.CreateAsync(user, command.Password);
            if (!result.Succeeded)
            {
                return AccountErrors.FromIdentity(result.Errors);
            }
        }
        catch (DbUpdateException exception) when (exception.InnerException is DbException { SqlState: UniqueViolationSqlState })
        {
            // Another sign-up with the same email won the race; the unique index caught it.
            return AccountErrors.EmailAlreadyRegistered;
        }

        auditLog.Record(AuditActions.UserCreated, AuditSubject.User(user.Id), actor: AuditActor.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return user;
    }
}
