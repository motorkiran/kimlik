using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Kimlik.Infrastructure.Identity;

/// <summary>Time-based one-time passwords as authenticator apps compute them (RFC 6238): HMAC-SHA1, 30-second steps, six digits.</summary>
internal static class Totp
{
    public const int Digits = 6;
    public static readonly TimeSpan StepLength = TimeSpan.FromSeconds(30);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / (long)StepLength.TotalSeconds;

    public static int Compute(ReadOnlySpan<byte> secret, long step)
    {
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
#pragma warning disable CA5350 // RFC 6238 and every authenticator app use HMAC-SHA1; HMAC keeps it sound.
        HMACSHA1.HashData(secret, counter, hash);
#pragma warning restore CA5350

        // Dynamic truncation (RFC 4226, section 5.3).
        var offset = hash[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash.Slice(offset, 4)) & 0x7FFFFFFF;
        return binary % 1_000_000;
    }

    /// <summary>Reads six digits, ignoring spaces people copy along.</summary>
    public static bool TryParseCode(string? input, out int code)
    {
        code = 0;
        var digits = input?.Replace(" ", string.Empty, StringComparison.Ordinal);
        return digits is { Length: Digits } && digits.All(char.IsAsciiDigit) && int.TryParse(digits, System.Globalization.CultureInfo.InvariantCulture, out code);
    }
}

/// <summary>Base32 as authenticator apps expect keys (RFC 4648, without padding).</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Decodes a key, ignoring case, spaces and padding; <see langword="null"/> when it is not Base32.</summary>
    public static byte[]? Decode(string input)
    {
        var output = new List<byte>(input.Length * 5 / 8);
        int buffer = 0, bits = 0;

        foreach (var character in input)
        {
            if (character is ' ' or '=')
            {
                continue;
            }

            var value = Alphabet.IndexOf(char.ToUpperInvariant(character), StringComparison.Ordinal);
            if (value < 0)
            {
                return null;
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return [.. output];
    }
}
