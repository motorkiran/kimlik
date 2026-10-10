using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Application.Users;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Accounts;

/// <summary>
/// A user's phone number: added or changed by entering the code texted to it, so Kimlik keeps verified numbers only,
/// each on one account at most; and removed by the user or an administrator.
/// </summary>
public sealed class UserPhoneNumbers(
    UserManager<User> userManager,
    IKimlikDbContext context,
    IOutbox outbox,
    ISmsThrottle throttle,
    IAuditLog auditLog,
    IOptions<SmsOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>Texts a code to <paramref name="phoneNumber"/>, as the user wrote it, and returns the number in E.164.</summary>
    public async Task<Result<string>> SendCodeAsync(Guid userId, string? phoneNumber, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return AccountErrors.SmsOff;
        }

        if (PhoneNumbers.Normalize(phoneNumber, options.Value.DefaultCountryCode) is not { } number)
        {
            return AccountErrors.InvalidPhoneNumber;
        }

        if (!PhoneNumbers.IsAllowed(number, options.Value))
        {
            return AccountErrors.PhoneNumberNotAllowed;
        }

        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (user.PhoneNumberConfirmed && user.PhoneNumber == number)
        {
            return AccountErrors.PhoneNumberUnchanged;
        }

        if (!throttle.TryAcquire(user.Id, AccountSms.PhoneVerification))
        {
            return AccountErrors.TooManyTexts;
        }

        outbox.Enqueue(new SendAccountSms(user.Id, AccountSms.PhoneVerification, number));
        await context.SaveChangesAsync(cancellationToken);
        return number;
    }

    /// <summary>
    /// Sets the number once the code texted to it is right. Wrong codes count toward the lockout, so they cannot be
    /// guessed; a number verified on another account is refused only then, so only its owner learns that.
    /// </summary>
    public async Task<Result> ConfirmAsync(Guid userId, string phoneNumber, string code, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return AccountErrors.SmsOff;
        }

        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return AccountErrors.WrongCode;
        }

        if (!await userManager.VerifyUserTokenAsync(
            user, OneTimeCodes.TokenProvider, OneTimeCodes.PhoneVerification(phoneNumber), code.Replace(" ", string.Empty, StringComparison.Ordinal)))
        {
            await userManager.AccessFailedAsync(user);
            return AccountErrors.WrongCode;
        }

        if (await context.Users.AnyAsync(other => other.Id != user.Id && other.PhoneNumber == phoneNumber && other.PhoneNumberConfirmed, cancellationToken))
        {
            return AccountErrors.PhoneNumberInUse;
        }

        user.SetVerifiedPhoneNumber(phoneNumber, timeProvider.GetUtcNow());
        auditLog.Record(AuditActions.UserPhoneNumberVerified, AuditSubject.User(user.Id));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Another account verified the number at the same moment.
            return AccountErrors.PhoneNumberInUse;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return Result.Success();
    }

    /// <summary>Removes the number; signing in with texts then stops for the account.</summary>
    public async Task<Result> RemoveAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (user.PhoneNumber is null)
        {
            return AccountErrors.PhoneNumberMissing;
        }

        user.SetVerifiedPhoneNumber(null, timeProvider.GetUtcNow());
        auditLog.Record(AuditActions.UserPhoneNumberRemoved, AuditSubject.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
