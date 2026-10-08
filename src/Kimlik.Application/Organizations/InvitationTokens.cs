using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Kimlik.Application.Organizations;

/// <summary>
/// Invitation tokens: 256 random bits, stored as a SHA-256 hash. A high-entropy secret needs no slow hash, so a
/// link is checked with a single indexed lookup.
/// </summary>
internal static class InvitationTokens
{
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
