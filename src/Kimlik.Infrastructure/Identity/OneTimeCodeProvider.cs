using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Users;
using Kimlik.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Infrastructure.Identity;

/// <summary>
/// One-time codes sent by email or text message (<see cref="OneTimeCodes"/>): six random digits that work once and for
/// ten minutes. Only a keyed hash of the latest code of each purpose is kept, with its expiry, among the user's Identity
/// tokens; a new code replaces it, and using it removes it.
/// </summary>
internal sealed class OneTimeCodeProvider(ISecretProtector protector, TimeProvider timeProvider) : IUserTwoFactorTokenProvider<User>
{
    private const string LoginProvider = "[KimlikOneTimeCode]";
    private const string HashPurpose = "identity.one-time-code";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<string> GenerateAsync(string purpose, UserManager<User> manager, User user)
    {
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var expiresAt = (timeProvider.GetUtcNow() + Lifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        await manager.SetAuthenticationTokenAsync(user, LoginProvider, OneTimeCodes.SlotOf(purpose), $"{expiresAt}:{Hash(user, purpose, code)}");
        return code;
    }

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<User> manager, User user)
    {
        var slot = OneTimeCodes.SlotOf(purpose);
        if (await manager.GetAuthenticationTokenAsync(user, LoginProvider, slot) is not { } stored
            || stored.Split(':') is not [var expires, var hash]
            || !long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
            || timeProvider.GetUtcNow().ToUnixTimeSeconds() >= expiresAt
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(Hash(user, purpose, token))))
        {
            return false;
        }

        await manager.RemoveAuthenticationTokenAsync(user, LoginProvider, slot);
        return true;
    }

    /// <summary>The codes sign people in or verify a number; they are not a second factor.</summary>
    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<User> manager, User user) => Task.FromResult(false);

    private string Hash(User user, string purpose, string code) =>
        Base64Url.EncodeToString(protector.Hash(Encoding.UTF8.GetBytes($"{user.Id:N}:{purpose}:{code}"), HashPurpose));
}
