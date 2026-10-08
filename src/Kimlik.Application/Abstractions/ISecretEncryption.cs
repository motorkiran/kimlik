namespace Kimlik.Application.Abstractions;

/// <summary>
/// Encrypts secrets that Kimlik must read back, such as webhook signing secrets, with the master key. Each secret is
/// bound to its purpose and to what it belongs to, so it cannot be moved to another record.
/// </summary>
public interface ISecretEncryption
{
    string Encrypt(string secret, string purpose, Guid owner);

    string Decrypt(string encryptedSecret, string purpose, Guid owner);
}
