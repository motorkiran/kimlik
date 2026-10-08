using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace Kimlik.Infrastructure.Security;

/// <summary>
/// Encrypts the ASP.NET Core Data Protection key ring with the master key before it is stored in the database.
/// </summary>
internal sealed class MasterKeyXmlEncryptor(ISecretProtector protector) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));

        try
        {
            var protectedBytes = protector.Protect(plaintext, SecretPurposes.DataProtectionKeyRing);

            var encryptedElement = new XElement(
                "encryptedKey",
                new XComment(" This key is encrypted with the Kimlik master key. "),
                new XElement("value", Convert.ToBase64String(protectedBytes)));

            return new EncryptedXmlInfo(encryptedElement, typeof(MasterKeyXmlDecryptor));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
