using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Accounts;

/// <summary>
/// Signing in with a one-time code sent by text message to the account's verified number, a first factor like a
/// password. As for email codes, nothing tells whether an account has the number: unknown numbers get no text, and their
/// codes are simply wrong.
/// </summary>
public sealed class PhoneSignIn(
    UserManager<User> userManager,
    IKimlikDbContext context,
    IOutbox outbox,
    ISmsThrottle throttle,
    IAuditLog auditLog,
    IOptions<SmsOptions> options)
{
    public bool Enabled => options.Value.Enabled;

    /// <summary>The number in E.164, or <see langword="null"/> when it is not one.</summary>
    public string? Normalize(string? phoneNumber) => PhoneNumbers.Normalize(phoneNumber, options.Value.DefaultCountryCode);

    /// <summary>Sends a code to the account with the verified number, if one can sign in; at most one a minute.</summary>
    public async Task RequestAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        if (Enabled
            && PhoneNumbers.IsAllowed(phoneNumber, options.Value)
            && await FindAsync(phoneNumber, cancellationToken) is { CanSignIn: true } user
            && throttle.TryAcquire(user.Id, AccountSms.SignInCode))
        {
            outbox.Enqueue(new SendAccountSms(user.Id, AccountSms.SignInCode, phoneNumber));
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The user whose code it is. A wrong code counts toward the lockout, as a wrong password does, and a locked account
    /// takes no code; the answer is the same whatever went wrong.
    /// </summary>
    public async Task<Result<User>> VerifyAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        var user = Enabled ? await FindAsync(phoneNumber, cancellationToken) : null;
        if (user is not { CanSignIn: true } || await userManager.IsLockedOutAsync(user))
        {
            return AccountErrors.WrongCode;
        }

        if (!await userManager.VerifyUserTokenAsync(user, OneTimeCodes.TokenProvider, OneTimeCodes.SmsSignIn, code.Replace(" ", string.Empty, StringComparison.Ordinal)))
        {
            await userManager.AccessFailedAsync(user);
            auditLog.Record(
                AuditActions.UserSignInFailed, AuditSubject.User(user.Id), new Dictionary<string, object?> { ["reason"] = "wrong_code" }, AuditActor.Anonymous);
            await context.SaveChangesAsync(cancellationToken);
            return AccountErrors.WrongCode;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return user;
    }

    private Task<User?> FindAsync(string phoneNumber, CancellationToken cancellationToken) =>
        userManager.Users.FirstOrDefaultAsync(user => user.PhoneNumber == phoneNumber && user.PhoneNumberConfirmed, cancellationToken);
}
