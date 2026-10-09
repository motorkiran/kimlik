using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Accounts;

/// <summary>
/// Signing in with a one-time code sent by email, a first factor like a password. Asking for a code queues the email;
/// its handler creates the code as it sends it, so no usable code is stored. Nothing tells whether an account has the
/// address: unknown addresses get no email, and their codes are simply wrong.
/// </summary>
public sealed class EmailSignIn(
    UserManager<User> userManager,
    IKimlikDbContext context,
    IOutbox outbox,
    IAccountEmailThrottle throttle,
    IAuditLog auditLog,
    IOptions<AccountOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>The Identity token provider that creates and checks the codes.</summary>
    public const string TokenProvider = "EmailSignIn";

    public const string Purpose = "sign-in";

    public const int CodeLength = 6;

    public bool Enabled => options.Value.EmailSignIn;

    /// <summary>Sends a code to the account with the address, if one can sign in; at most one a minute.</summary>
    public async Task RequestAsync(string email, CancellationToken cancellationToken)
    {
        if (Enabled
            && await userManager.FindByEmailAsync(email) is { CanSignIn: true } user
            && throttle.TryAcquire(user.Id, AccountEmail.SignInCode))
        {
            outbox.Enqueue(new SendAccountEmail(user.Id, AccountEmail.SignInCode));
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The user whose code it is. A wrong code counts toward the lockout, as a wrong password does, and a locked account
    /// takes no code; the answer is the same whatever went wrong. A right code also verifies the address.
    /// </summary>
    public async Task<Result<User>> VerifyAsync(string email, string code, CancellationToken cancellationToken)
    {
        var user = Enabled ? await userManager.FindByEmailAsync(email) : null;
        if (user is not { CanSignIn: true } || await userManager.IsLockedOutAsync(user))
        {
            return AccountErrors.WrongCode;
        }

        var normalized = code.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!await userManager.VerifyUserTokenAsync(user, TokenProvider, Purpose, normalized))
        {
            await userManager.AccessFailedAsync(user);
            auditLog.Record(
                AuditActions.UserSignInFailed, AuditSubject.User(user.Id), new Dictionary<string, object?> { ["reason"] = "wrong_code" }, AuditActor.Anonymous);
            await context.SaveChangesAsync(cancellationToken);
            return AccountErrors.WrongCode;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        if (!user.EmailConfirmed)
        {
            user.MarkEmailVerified(timeProvider.GetUtcNow());
            await userManager.UpdateAsync(user);
            auditLog.Record(AuditActions.UserEmailVerified, AuditSubject.User(user.Id), actor: AuditActor.User(user.Id));
            await context.SaveChangesAsync(cancellationToken);
        }

        return user;
    }
}
