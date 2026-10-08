using System.Text;
using Kimlik.Application.Abstractions;

namespace Kimlik.Infrastructure.Security;

internal sealed class SecretEncryption(ISecretProtector protector) : ISecretEncryption
{
    public string Encrypt(string secret, string purpose, Guid owner) =>
        Convert.ToBase64String(protector.Protect(Encoding.UTF8.GetBytes(secret), purpose, owner.ToByteArray()));

    public string Decrypt(string encryptedSecret, string purpose, Guid owner) =>
        Encoding.UTF8.GetString(protector.Unprotect(Convert.FromBase64String(encryptedSecret), purpose, owner.ToByteArray()));
}
