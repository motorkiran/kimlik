using System.Globalization;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Infrastructure.Identity;

/// <summary>
/// Verifies codes from authenticator apps, tolerating one 30-second step of clock drift either way. Each code works
/// once: the last accepted step is stored, and a code from it or an earlier step is refused (RFC 6238, section 5.2).
/// Saving that step goes through the user's concurrency stamp, so two requests racing with one code cannot both win.
/// </summary>
internal sealed class TotpTokenProvider(TimeProvider timeProvider) : IUserTwoFactorTokenProvider<User>
{
    /// <summary>Where Kimlik keeps its own per-user two-factor state among Identity's user tokens.</summary>
    internal const string TokenLoginProvider = "[Kimlik]";
    internal const string LastStepTokenName = "TotpLastStep";

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<User> manager, User user) =>
        await manager.GetAuthenticatorKeyAsync(user) is { Length: > 0 };

    /// <summary>Authenticator apps generate the codes; Kimlik never sends one.</summary>
    public Task<string> GenerateAsync(string purpose, UserManager<User> manager, User user) => Task.FromResult(string.Empty);

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<User> manager, User user)
    {
        if (!Totp.TryParseCode(token, out var code) || await manager.GetAuthenticatorKeyAsync(user) is not { } key || Base32.Decode(key) is not { } secret)
        {
            return false;
        }

        var lastAccepted = long.TryParse(await manager.GetAuthenticationTokenAsync(user, TokenLoginProvider, LastStepTokenName), CultureInfo.InvariantCulture, out var last)
            ? last
            : long.MinValue;

        var current = Totp.StepAt(timeProvider.GetUtcNow());
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step > lastAccepted && Totp.Compute(secret, step) == code)
            {
                var stored = await manager.SetAuthenticationTokenAsync(user, TokenLoginProvider, LastStepTokenName, step.ToString(CultureInfo.InvariantCulture));
                return stored.Succeeded;
            }
        }

        return false;
    }
}
