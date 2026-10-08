using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Server.Identity;

/// <summary>
/// Spends the time of a password verification when no account matches, so response times do not reveal
/// which email addresses are registered.
/// </summary>
public sealed class PasswordHashTiming(IPasswordHasher<User> passwordHasher)
{
    private static readonly User Placeholder = User.Create("placeholder@kimlik.invalid", null, null, null, DateTimeOffset.UnixEpoch);

    // Computed once with the configured hashing cost; a race only computes it twice.
    private static string? _placeholderHash;

    public void VerifyAgainstPlaceholder(string password)
    {
        var hash = _placeholderHash ??= passwordHasher.HashPassword(Placeholder, Guid.NewGuid().ToString());
        passwordHasher.VerifyHashedPassword(Placeholder, hash, password);
    }
}
