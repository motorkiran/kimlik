namespace Kimlik.Infrastructure.Security;

/// <summary>
/// Purposes passed to <see cref="ISecretProtector"/>. They feed the key derivation, so changing a value
/// makes existing ciphertext unreadable.
/// </summary>
internal static class SecretPurposes
{
    public const string DataProtectionKeyRing = "Kimlik.DataProtection.KeyRing.v1";
    public const string TokenKeys = "Kimlik.TokenKeys.v1";
}
