using System.Globalization;
using Kimlik.Infrastructure.Identity;

namespace Kimlik.Server.Tests.Mfa;

/// <summary>Computes authenticator codes the way an app would, for a key Kimlik handed out.</summary>
internal static class TotpCodes
{
    /// <summary>
    /// Codes for the previous, current and next 30-second steps, which Kimlik accepts in that order. It waits out the
    /// end of a step first, so all three stay within the accepted window while a test uses them.
    /// </summary>
    public static async Task<string[]> NextThreeAsync(string key)
    {
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30 >= 20)
        {
            await Task.Delay(TimeSpan.FromSeconds(11), TestContext.Current.CancellationToken);
        }

        var secret = Base32.Decode(key)!;
        var step = Totp.StepAt(DateTimeOffset.UtcNow);
        return [.. new[] { step - 1, step, step + 1 }.Select(candidate => Totp.Compute(secret, candidate).ToString("D6", CultureInfo.InvariantCulture))];
    }
}
