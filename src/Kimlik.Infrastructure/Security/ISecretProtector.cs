namespace Kimlik.Infrastructure.Security;

/// <summary>
/// Encrypts secrets that must be read back later (signing keys, the Data Protection key ring, webhook secrets),
/// and hashes secrets that only need to be checked, with keys derived from the master key. Each purpose gets its
/// own key, so output produced for one purpose is useless for another.
/// </summary>
internal interface ISecretProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose, ReadOnlySpan<byte> associatedData = default);

    /// <exception cref="System.Security.Cryptography.CryptographicException">The data was tampered with or protected for another purpose.</exception>
    byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose, ReadOnlySpan<byte> associatedData = default);

    /// <summary>
    /// An HMAC-SHA256 of <paramref name="data"/>. Unlike a plain hash, it cannot be brute-forced without the master
    /// key, which matters for short secrets such as recovery codes.
    /// </summary>
    byte[] Hash(ReadOnlySpan<byte> data, string purpose);
}
