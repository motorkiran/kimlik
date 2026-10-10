using Kimlik.Application.Abstractions;
using Kimlik.Application.Branding;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Accounts;

public enum AccountSms
{
    /// <summary>A code to sign in with, to the account's verified number.</summary>
    SignInCode,

    /// <summary>A code that verifies a number the user is adding to their account.</summary>
    PhoneVerification,
}

/// <summary>
/// Limits how many texts one account receives, so that forms cannot be used to flood a phone or to run up the bill:
/// one of each kind a minute, and a few an hour.
/// </summary>
public interface ISmsThrottle
{
    /// <returns><see langword="false"/> when the account has had enough texts for now.</returns>
    bool TryAcquire(Guid userId, AccountSms kind);
}

/// <summary>
/// Sends a code by text message. As for emails, only the intent is stored in the outbox; the code is created as the text
/// goes out, so no usable code sits in the database.
/// </summary>
[OutboxMessage("account.sms")]
public sealed record SendAccountSms(Guid UserId, AccountSms Kind, string PhoneNumber);

public sealed class SendAccountSmsHandler(UserManager<User> userManager, IOptions<BrandingOptions> branding, ISmsSender sender)
    : IOutboxMessageHandler<SendAccountSms>
{
    public async Task HandleAsync(SendAccountSms message, CancellationToken cancellationToken)
    {
        // The account was deleted while the message was waiting, or no longer has the number.
        if (await userManager.FindByIdAsync(message.UserId.ToString()) is not { } user
            || (message.Kind == AccountSms.SignInCode && (!user.PhoneNumberConfirmed || user.PhoneNumber != message.PhoneNumber)))
        {
            return;
        }

        var purpose = message.Kind == AccountSms.SignInCode ? OneTimeCodes.SmsSignIn : OneTimeCodes.PhoneVerification(message.PhoneNumber);
        var code = await userManager.GenerateUserTokenAsync(user, OneTimeCodes.TokenProvider, purpose);
        await sender.SendAsync(new SmsMessage(message.PhoneNumber, Text(message.Kind, user.Locale, code, branding.Value.ProductName)), cancellationToken);
    }

    /// <summary>A short text in the account's language, which names the product and warns against sharing the code.</summary>
    internal static string Text(AccountSms kind, string? locale, string code, string product)
    {
        var turkish = locale?.StartsWith("tr", StringComparison.OrdinalIgnoreCase) == true;
        return (kind, turkish) switch
        {
            (AccountSms.SignInCode, false) => $"{code} is your {product} sign-in code. It expires in 10 minutes. Do not share it with anyone.",
            (AccountSms.SignInCode, true) => $"{product} giriş kodunuz: {code}. 10 dakika geçerlidir. Kimseyle paylaşmayın.",
            (_, false) => $"{code} is your {product} code to verify this phone number. It expires in 10 minutes. Do not share it with anyone.",
            (_, true) => $"{product} telefon numarası doğrulama kodunuz: {code}. 10 dakika geçerlidir. Kimseyle paylaşmayın.",
        };
    }
}
