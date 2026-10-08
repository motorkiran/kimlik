namespace Kimlik.Infrastructure.Security;

/// <summary>
/// Encrypts secrets that must be read back later (signing keys, the Data Protection key ring, webhook secrets)
/// with keys derived from the master key. Each purpose gets its own key, so ciphertext produced for one purpose
/// cannot be decrypted under another.
/// </summary>
internal interface ISecretProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose, ReadOnlySpan<byte> associatedData = default);

    /// <exception cref="System.Security.Cryptography.CryptographicException">The data was tampered with or protected for another purpose.</exception>
    byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose, ReadOnlySpan<byte> associatedData = default);
}
