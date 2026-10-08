using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Infrastructure.Security.TokenKeys;

internal sealed record LoadedTokenKey(string KeyId, TokenKeyUse Use, SecurityKey SecurityKey, bool IsActive);

/// <summary>An immutable snapshot of the usable token keys at a point in time.</summary>
internal sealed class TokenKeySet
{
    private TokenKeySet(IReadOnlyList<LoadedTokenKey> keys)
    {
        Keys = keys;
        Fingerprint = string.Join(';', keys.Select(key => $"{key.KeyId}:{key.IsActive}").Order(StringComparer.Ordinal));
    }

    public IReadOnlyList<LoadedTokenKey> Keys { get; }

    /// <summary>Identifies the keys and which of them are active; equal fingerprints mean nothing changed.</summary>
    public string Fingerprint { get; }

    public IEnumerable<LoadedTokenKey> SigningKeys => Keys.Where(key => key.Use == TokenKeyUse.Signing);

    public IEnumerable<LoadedTokenKey> EncryptionKeys => Keys.Where(key => key.Use == TokenKeyUse.Encryption);

    public LoadedTokenKey ActiveKey(TokenKeyUse use) => Keys.First(key => key.Use == use && key.IsActive);

    /// <summary>
    /// Builds a snapshot from the stored keys, reusing already loaded security keys so that unchanged keys
    /// are not decrypted again.
    /// </summary>
    public static TokenKeySet Create(IEnumerable<TokenKey> keys, DateTimeOffset now, TokenKeySet? previous, Func<TokenKey, SecurityKey> load)
    {
        var loaded = previous?.Keys.ToDictionary(key => key.KeyId, key => key.SecurityKey, StringComparer.Ordinal) ?? [];

        var activeKeyIds = keys
            .Where(key => TokenKeyPlanner.IsActive(key, now))
            .GroupBy(key => key.Use)
            .Select(group => group.MaxBy(key => key.ActivatesAt)!.KeyId)
            .ToHashSet(StringComparer.Ordinal);

        var snapshot = keys
            .Where(key => key.ExpiresAt > now)
            .OrderBy(key => key.Use)
            .ThenByDescending(key => activeKeyIds.Contains(key.KeyId))
            .ThenByDescending(key => key.ActivatesAt)
            .Select(key => new LoadedTokenKey(
                key.KeyId,
                key.Use,
                loaded.TryGetValue(key.KeyId, out var securityKey) ? securityKey : load(key),
                activeKeyIds.Contains(key.KeyId)))
            .ToList();

        return new TokenKeySet(snapshot);
    }
}
