using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Accounts;

/// <summary>
/// A sign-up with an account at another provider. <c>EmailVerified</c> is set when a provider that Kimlik trusts
/// verified the address; otherwise the person verifies it with Kimlik, as after a sign-up with a password.
/// </summary>
public sealed record RegisterExternalUserCommand(
    ExternalLogin Login, string Email, bool EmailVerified, string? GivenName, string? FamilyName, string? Locale, string? ReturnUrl = null);

/// <summary>
/// Creates an account, without a password, that signs in with an account at another provider. When registration is
/// invite-only, the provider must have verified an address that has an open invitation.
/// </summary>
public sealed class RegisterExternalUserHandler(
    UserManager<User> userManager,
    ExternalLogins logins,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOutbox outbox,
    DefaultUserRoles defaultRoles,
    IOptions<AccountOptions> options,
    TimeProvider timeProvider)
{
    public async Task<Result<User>> HandleAsync(RegisterExternalUserCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var registration = options.Value.Registration;
        if (registration == RegistrationMode.Disabled
            || (registration == RegistrationMode.InviteOnly && !(command.EmailVerified && await IsInvitedAsync(command.Email, now, cancellationToken))))
        {
            return AccountErrors.RegistrationClosed;
        }

        var user = User.Create(command.Email, command.GivenName, command.FamilyName, command.Locale, now);
        user.EmailConfirmed = command.EmailVerified;

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        try
        {
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
            {
                return AccountErrors.FromIdentity(created.Errors);
            }
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Another sign-up with the same address won the race; the unique index caught it.
            return AccountErrors.EmailAlreadyRegistered;
        }

        var linked = await logins.AddAsync(user, command.Login);
        if (linked.IsFailure)
        {
            return linked.Error;
        }

        var provider = new Dictionary<string, object?> { ["provider"] = command.Login.Provider };
        auditLog.Record(AuditActions.UserCreated, AuditSubject.User(user.Id), provider, AuditActor.User(user.Id));
        auditLog.Record(AuditActions.UserLoginLinked, AuditSubject.User(user.Id), provider, AuditActor.User(user.Id));
        await defaultRoles.AssignAsync(user.Id, cancellationToken);
        if (!user.EmailConfirmed && options.Value.RequireVerifiedEmail)
        {
            outbox.Enqueue(new SendAccountEmail(user.Id, AccountEmail.EmailVerification, command.ReturnUrl));
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    private Task<bool> IsInvitedAsync(string email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var normalizedEmail = userManager.NormalizeEmail(email);
        return context.Invitations.AnyAsync(
            invitation => invitation.NormalizedEmail == normalizedEmail && invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt > now,
            cancellationToken);
    }
}
