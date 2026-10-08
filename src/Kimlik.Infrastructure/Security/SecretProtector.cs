using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Security;

/// <summary>
/// AES-256-GCM with per-purpose keys derived from the master key through HKDF-SHA256.
/// Output format: <c>version (1 byte) | nonce (12 bytes) | ciphertext | tag (16 bytes)</c>.
/// </summary>
internal sealed class SecretProtector : ISecretProtector
{
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize;

    private readonly byte[] _masterKey;
    private readonly ConcurrentDictionary<string, byte[]> _purposeKeys = new(StringComparer.Ordinal);

    public SecretProtector(IOptions<SecurityOptions> options) =>
        _masterKey = Convert.FromBase64String(options.Value.MasterKey);

    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose, ReadOnlySpan<byte> associatedData = default)
    {
        var output = new byte[HeaderSize + plaintext.Length + TagSize];
        output[0] = FormatVersion;

        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(GetPurposeKey(purpose), TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(HeaderSize, plaintext.Length), output.AsSpan(HeaderSize + plaintext.Length), associatedData);

        return output;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose, ReadOnlySpan<byte> associatedData = default)
    {
        if (protectedData.Length < HeaderSize + TagSize || protectedData[0] != FormatVersion)
        {
            throw new CryptographicException("The protected data is malformed or uses an unsupported format version.");
        }

        var ciphertextLength = protectedData.Length - HeaderSize - TagSize;
        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(GetPurposeKey(purpose), TagSize);
        aes.Decrypt(
            protectedData.Slice(1, NonceSize),
            protectedData.Slice(HeaderSize, ciphertextLength),
            protectedData.Slice(HeaderSize + ciphertextLength),
            plaintext,
            associatedData);

        return plaintext;
    }

    private byte[] GetPurposeKey(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        return _purposeKeys.GetOrAdd(purpose, static (value, masterKey) =>
            HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, outputLength: 32, salt: [], info: Encoding.UTF8.GetBytes(value)),
            _masterKey);
    }
}
