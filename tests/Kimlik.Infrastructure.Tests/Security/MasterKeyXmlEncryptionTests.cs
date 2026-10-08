using System.Security.Cryptography;
using System.Xml.Linq;
using Kimlik.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Infrastructure.Tests.Security;

public sealed class MasterKeyXmlEncryptionTests
{
    private static readonly XElement KeyElement = new(
        "masterKey",
        new XElement("value", "c2VjcmV0LWtleS1tYXRlcmlhbA=="));

    private readonly SecretProtector _protector = SecretProtectorTests.CreateProtector(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Encrypt_HidesKeyMaterial_AndNamesTheDecryptor()
    {
        var encrypted = new MasterKeyXmlEncryptor(_protector).Encrypt(KeyElement);

        encrypted.DecryptorType.ShouldBe(typeof(MasterKeyXmlDecryptor));
        encrypted.EncryptedElement.ToString().ShouldNotContain("c2VjcmV0LWtleS1tYXRlcmlhbA==");
    }

    [Fact]
    public void Decrypt_RestoresOriginalElement()
    {
        var encrypted = new MasterKeyXmlEncryptor(_protector).Encrypt(KeyElement);
        var services = new ServiceCollection().AddSingleton<ISecretProtector>(_protector).BuildServiceProvider();

        var decrypted = new MasterKeyXmlDecryptor(services).Decrypt(encrypted.EncryptedElement);

        XNode.DeepEquals(decrypted, KeyElement).ShouldBeTrue();
    }
}
