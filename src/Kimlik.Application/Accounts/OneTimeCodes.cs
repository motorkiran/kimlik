namespace Kimlik.Application.Accounts;

/// <summary>
/// The one-time codes Kimlik sends by email or text message: six digits, created as they go out, working once and for
/// ten minutes. Each purpose keeps only its latest code.
/// </summary>
public static class OneTimeCodes
{
    /// <summary>The Identity token provider that creates and checks the codes.</summary>
    public const string TokenProvider = "OneTimeCode";

    public const int Length = 6;

    /// <summary>Signing in with a code sent by email.</summary>
    public const string EmailSignIn = "sign-in";

    /// <summary>Signing in with a code sent by text message to the account's verified number.</summary>
    public const string SmsSignIn = "sms-sign-in";

    private const string PhoneVerificationPrefix = "verify-phone:";

    /// <summary>Verifying that <paramref name="phoneNumber"/> is the user's; a code for one number does not verify another.</summary>
    public static string PhoneVerification(string phoneNumber) => PhoneVerificationPrefix + phoneNumber;

    /// <summary>Where a purpose keeps its latest code: verifying any number replaces the code for verifying another.</summary>
    public static string SlotOf(string purpose) =>
        purpose.StartsWith(PhoneVerificationPrefix, StringComparison.Ordinal) ? PhoneVerificationPrefix.TrimEnd(':') : purpose;
}
