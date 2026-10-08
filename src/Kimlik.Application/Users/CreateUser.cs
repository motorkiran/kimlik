using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DomainUser = Kimlik.Domain.Users.User;

namespace Kimlik.Application.Users;

/// <summary>Creates a user on behalf of an administrator or an application backend.</summary>
public sealed class CreateUserHandler(
    UserManager<DomainUser> userManager,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOutbox outbox,
    IOptions<AccountOptions> accounts,
    TimeProvider timeProvider)
{
    public async Task<Result<UserResponse>> HandleAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = DomainUser.Create(request.Email, request.GivenName, request.FamilyName, request.Locale, now);
        if (request.EmailVerified)
        {
            user.MarkEmailVerified(now);
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = request.Password is null
                ? await userManager.CreateAsync(user)
                : await userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                return AccountErrors.FromIdentity(result.Errors);
            }
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return AccountErrors.EmailAlreadyRegistered;
        }

        auditLog.Record(AuditActions.UserCreated, AuditSubject.User(user.Id));
        if (!user.EmailConfirmed && accounts.Value.RequireVerifiedEmail)
        {
            outbox.Enqueue(new SendAccountEmail(user.Id, AccountEmail.EmailVerification));
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return user.ToResponse([]);
    }
}
