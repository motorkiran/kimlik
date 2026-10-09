using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Application.Organizations;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Accounts;

/// <summary>
/// A sign-up. <c>ReturnUrl</c> is where to continue after the email address is verified, typically a pending
/// authorization request. <c>InvitationToken</c> comes from the link of an invitation to the address. Without a
/// <c>Password</c>, the person signs in with codes sent to the address.
/// </summary>
public sealed record RegisterUserCommand(
    string Email, string? Password, string? GivenName, string? FamilyName, string? Locale, string? ReturnUrl = null, string? InvitationToken = null);

/// <summary>
/// Creates an account through self-service sign-up: for anyone when registration is open, and for people invited to
/// an organization when it is invite-only. The invitation link reached the invited inbox, so it also verifies the address.
/// </summary>
public sealed class RegisterUserHandler(
    UserManager<User> userManager,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOutbox outbox,
    DefaultUserRoles defaultRoles,
    EmailSignIn emailSignIn,
    IAccountEmailThrottle throttle,
    IOptions<AccountOptions> options,
    TimeProvider timeProvider)
{
    public async Task<Result<User>> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        if (command.Password is null && !emailSignIn.Enabled)
        {
            return AccountErrors.PasswordMissing;
        }

        var now = timeProvider.GetUtcNow();
        var invited = command.InvitationToken is { Length: > 0 } token
            && await FindInvitationHandler.FindOpenAsync(context, token, now, cancellationToken) is { } invitation
            && invitation.NormalizedEmail == userManager.NormalizeEmail(command.Email);

        var registration = options.Value.Registration;
        if (registration == RegistrationMode.Disabled || (registration == RegistrationMode.InviteOnly && !invited))
        {
            return AccountErrors.RegistrationClosed;
        }

        var user = User.Create(command.Email, command.GivenName, command.FamilyName, command.Locale, now);
        if (invited)
        {
            user.MarkEmailVerified(now);
        }

        var error = await CreateAsync(user, command, cancellationToken);
        if (error is null)
        {
            return user;
        }

        if (error == AccountErrors.EmailAlreadyRegistered && options.Value.RequireVerifiedEmail)
        {
            // Someone without a password just wants in: the owner of the address gets a code, as when signing in.
            await (command.Password is null
                ? emailSignIn.RequestAsync(command.Email, cancellationToken)
                : NotifyExistingAccountAsync(command.Email, cancellationToken));
        }

        return error;
    }

    /// <summary>Creates the user, its audit event and its verification email in one transaction.</summary>
    private async Task<Error?> CreateAsync(User user, RegisterUserCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = command.Password is null ? await userManager.CreateAsync(user) : await userManager.CreateAsync(user, command.Password);
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
        if (!user.EmailConfirmed && options.Value.RequireVerifiedEmail)
        {
            // Without a password, the code that signs the person in also verifies the address.
            var email = command.Password is null ? AccountEmail.SignInCode : AccountEmail.EmailVerification;
            outbox.Enqueue(new SendAccountEmail(user.Id, email, command.ReturnUrl));
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
        if (await userManager.FindByEmailAsync(email) is { } existing
            && throttle.TryAcquire(existing.Id, AccountEmail.AlreadyRegistered))
        {
            outbox.Enqueue(new SendAccountEmail(existing.Id, AccountEmail.AlreadyRegistered));
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
