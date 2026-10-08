using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Kimlik.Application.ApiKeys;

/// <summary>
/// API key secrets: <c>kmk_</c> and 256 random bits. The prefix lets secret scanners and people recognize them. Being
/// random, they need no slow hashing; SHA-256 is enough to keep them unreadable at rest and quick to look up.
/// </summary>
internal static class ApiKeySecrets
{
    public const string Prefix = "kmk_";

    /// <summary>How much of the secret is kept in the clear, to recognize the key by: the prefix and eight characters.</summary>
    private const int DisplayLength = 12;

    /// <summary>The prefix and 43 base64url characters.</summary>
    private const int Length = 47;

    public static (string Secret, string DisplayPrefix, string Hash) Generate()
    {
        var secret = Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (secret, secret[..DisplayLength], Hash(secret));
    }

    /// <summary>Whether the value could be a key at all, so that anything else is turned away without a lookup.</summary>
    public static bool IsWellFormed(string? value) =>
        value is { Length: Length } && value.StartsWith(Prefix, StringComparison.Ordinal) && Base64Url.IsValid(value.AsSpan(Prefix.Length));

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
