namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>
/// A key used by the OpenID Connect server: an asymmetric key that signs tokens (its public part is published
/// in JWKS) or a symmetric key that encrypts the tokens only Kimlik reads, such as authorization codes and
/// refresh tokens. The key material is encrypted with the master key.
/// </summary>
public sealed class TokenKey
{
    public const int KeyIdMaxLength = 64;
    public const int AlgorithmMaxLength = 16;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The <c>kid</c> published in JWKS and token headers.</summary>
    public required string KeyId { get; init; }

    public required TokenKeyUse Use { get; init; }

    public required string Algorithm { get; init; }

    /// <summary>PKCS#8 private key (signing) or raw key bytes (encryption), protected with the master key.</summary>
    public required byte[] ProtectedKey { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the key starts signing or encrypting. Before that it is only published, so caches can pick it up.</summary>
    public required DateTimeOffset ActivatesAt { get; init; }

    /// <summary>When the key stops signing or encrypting new tokens.</summary>
    public required DateTimeOffset RetiresAt { get; init; }

    /// <summary>When the key is no longer needed to validate or decrypt tokens issued before retirement.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
