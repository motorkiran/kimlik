using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Kimlik.Contracts.Management;

namespace Kimlik.Application.ApiKeys;

/// <summary>
/// API key secrets, as <see cref="ApiKeyFormat"/> describes them. Being random, they need no slow hashing; SHA-256 is
/// enough to keep them unreadable at rest and quick to look up.
/// </summary>
internal static class ApiKeySecrets
{
    /// <summary>How much of the secret is kept in the clear, to recognize the key by: the prefix and eight characters.</summary>
    private const int DisplayLength = 12;

    public static (string Secret, string DisplayPrefix, string Hash) Generate()
    {
        var secret = ApiKeyFormat.Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (secret, secret[..DisplayLength], Hash(secret));
    }

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
