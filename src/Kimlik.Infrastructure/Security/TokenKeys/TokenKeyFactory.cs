using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>
/// Generates token keys and turns stored keys back into security keys. Key material only exists unencrypted
/// in memory; at rest it is protected with the master key and bound to the key ID.
/// </summary>
internal sealed class TokenKeyFactory(ISecretProtector protector, TimeProvider timeProvider)
{
    private const int RsaKeySizeInBits = 3072;
    private const int EncryptionKeySizeInBytes = 32;

    public TokenKey Create(TokenKeySchedule schedule) => schedule.Use switch
    {
        TokenKeyUse.Signing => CreateSigningKey(schedule),
        TokenKeyUse.Encryption => CreateEncryptionKey(schedule),
        _ => throw new ArgumentOutOfRangeException(nameof(schedule), schedule.Use, "Unknown token key use."),
    };

    public SecurityKey Load(TokenKey key)
    {
        var material = protector.Unprotect(key.ProtectedKey, SecretPurposes.TokenKeys, Encoding.UTF8.GetBytes(key.KeyId));

        if (key.Use == TokenKeyUse.Encryption)
        {
            return new SymmetricSecurityKey(material) { KeyId = key.KeyId };
        }

        try
        {
            var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(material, out _);
            return new RsaSecurityKey(rsa) { KeyId = key.KeyId };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private TokenKey CreateSigningKey(TokenKeySchedule schedule)
    {
        using var rsa = RSA.Create(RsaKeySizeInBits);

        // The key ID is the RFC 7638 thumbprint of the public key.
        var publicKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false));
        var keyId = Base64UrlEncoder.Encode(JsonWebKeyConverter.ConvertFromRSASecurityKey(publicKey).ComputeJwkThumbprint());

        var privateKey = rsa.ExportPkcs8PrivateKey();
        try
        {
            return Protect(schedule, keyId, SecurityAlgorithms.RsaSha256, privateKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private TokenKey CreateEncryptionKey(TokenKeySchedule schedule)
    {
        var keyId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16));
        var key = RandomNumberGenerator.GetBytes(EncryptionKeySizeInBytes);
        try
        {
            return Protect(schedule, keyId, SecurityAlgorithms.Aes256KW, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private TokenKey Protect(TokenKeySchedule schedule, string keyId, string algorithm, byte[] material) => new()
    {
        KeyId = keyId,
        Use = schedule.Use,
        Algorithm = algorithm,
        ProtectedKey = protector.Protect(material, SecretPurposes.TokenKeys, Encoding.UTF8.GetBytes(keyId)),
        CreatedAt = timeProvider.GetUtcNow(),
        ActivatesAt = schedule.ActivatesAt,
        RetiresAt = schedule.RetiresAt,
        ExpiresAt = schedule.ExpiresAt,
    };
}
