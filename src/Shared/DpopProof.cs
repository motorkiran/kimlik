using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Security;

/// <summary>
/// A DPoP proof that passed every check of RFC 9449, section 4.3, but whether it was used before: the caller keeps
/// <see cref="ReplayKey"/> until <see cref="UsableUntil"/>. Compiled into Kimlik and into Kimlik.AspNetCore.
/// </summary>
internal sealed class DpopProof
{
    /// <summary>The request header with the proof.</summary>
    public const string HeaderName = "DPoP";

    /// <summary>The authorization scheme of DPoP-bound access tokens.</summary>
    public const string Scheme = "DPoP";

    /// <summary>The error for a missing or invalid proof.</summary>
    public const string InvalidProofError = "invalid_dpop_proof";

    /// <summary>A proof is accepted this long after it was made.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    /// <summary>And this long before, for clocks that run ahead.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(1);

    public static readonly IReadOnlyList<string> Algorithms =
        [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSha256];

    private const string Type = "dpop+jwt";
    private const int MaxLength = 8192;
    private const int MaxJtiLength = 256;
    private const int MinRsaKeyBytes = 2048 / 8;
    private const int MaxRsaKeyBytes = 8192 / 8;

    // Every proof brings a key of its own, which a cache would only fill up with.
    private static readonly CryptoProviderFactory Verifiers = new() { CacheSignatureProviders = false };

    private DpopProof(string thumbprint, string replayKey, DateTimeOffset usableUntil)
    {
        Thumbprint = thumbprint;
        ReplayKey = replayKey;
        UsableUntil = usableUntil;
    }

    /// <summary>The JWK SHA-256 thumbprint (RFC 7638) of the proof's key, which bound tokens carry as <c>cnf.jkt</c>.</summary>
    public string Thumbprint { get; }

    /// <summary>The hex SHA-256 hash of the key and <c>jti</c>, which tells the proof apart from every other one.</summary>
    public string ReplayKey { get; }

    /// <summary>Until when the proof would be accepted, and has to be remembered.</summary>
    public DateTimeOffset UsableUntil { get; }

    /// <summary>The thumbprint of a key from the <c>cnf</c> claim of an access token (<c>{"jkt": "…"}</c>), if it is bound to one.</summary>
    public static string? BoundKeyOf(string? confirmation)
    {
        if (string.IsNullOrEmpty(confirmation))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(confirmation);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("jkt", out var thumbprint) && thumbprint.ValueKind == JsonValueKind.String
                ? thumbprint.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The SHA-256 hash of an access token, as proofs that come with it carry it (<c>ath</c>).</summary>
    public static string HashOf(string accessToken) => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));

    /// <summary>
    /// Reads and checks the proof in <paramref name="headers"/>, made for a <paramref name="method"/> request to
    /// <paramref name="target"/>, and, with <paramref name="accessToken"/>, for that token. Returns <see langword="null"/>
    /// with an <paramref name="error"/> for an invalid proof, and without one if there is none.
    /// </summary>
    public static DpopProof? Read(StringValues headers, string method, Uri target, string? accessToken, DateTimeOffset now, out string? error)
    {
        error = null;
        if (headers.Count == 0)
        {
            return null;
        }

        if (headers.Count > 1 || headers[0] is not { Length: > 0 and <= MaxLength } value)
        {
            error = "Send one DPoP proof.";
            return null;
        }

        error = Check(value, method, target, accessToken, now, out var proof);
        return proof;
    }

    private static string? Check(string value, string method, Uri target, string? accessToken, DateTimeOffset now, out DpopProof? proof)
    {
        proof = null;

        JsonWebToken token;
        JsonWebKey key;
        try
        {
            token = new JsonWebToken(value);
            if (token.IsEncrypted || !string.Equals(token.Typ, Type, StringComparison.OrdinalIgnoreCase) || !Algorithms.Contains(token.Alg))
            {
                return $"The DPoP proof must be a JWT typed {Type}, signed with {string.Join(", ", Algorithms)}.";
            }

            using var header = JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedHeader));
            if (!header.RootElement.TryGetProperty("jwk", out var jwk) || jwk.ValueKind != JsonValueKind.Object)
            {
                return "The DPoP proof must carry its public key in the jwk header.";
            }

            key = new JsonWebKey(jwk.GetRawText());
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or FormatException or SecurityTokenException)
        {
            return "The DPoP proof is not a valid JWT.";
        }

        if (!IsPublicKeyFor(key, token.Alg))
        {
            return "The DPoP proof's key must be a public EC key on P-256 or P-384, or a public RSA key of at least 2048 bits, that fits its algorithm.";
        }

        if (!IsSignedBy(token, key))
        {
            return "The DPoP proof's signature is not valid.";
        }

        if (!token.TryGetPayloadValue<string>("jti", out var jti) || jti is not { Length: > 0 and <= MaxJtiLength }
            || !token.TryGetPayloadValue<long>("iat", out var iat)
            || !token.TryGetPayloadValue<string>("htm", out var htm)
            || !token.TryGetPayloadValue<string>("htu", out var htu))
        {
            return "The DPoP proof must have jti, iat, htm and htu claims.";
        }

        if (!string.Equals(htm, method, StringComparison.Ordinal) || !IsTarget(htu, target))
        {
            return "The DPoP proof was made for another request.";
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(iat, 0, DateTimeOffset.MaxValue.ToUnixTimeSeconds() - 1));
        if (issuedAt > now + MaxClockSkew || issuedAt < now - MaxAge)
        {
            return "The DPoP proof is too old, or made in the future.";
        }

        if (accessToken is not null
            && (!token.TryGetPayloadValue<string>("ath", out var ath) || !string.Equals(ath, HashOf(accessToken), StringComparison.Ordinal)))
        {
            return "The DPoP proof was made for another access token.";
        }

        var thumbprint = Base64UrlEncoder.Encode(key.ComputeJwkThumbprint());
        var replayKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{thumbprint}.{jti}")));
        proof = new DpopProof(thumbprint, replayKey, issuedAt + MaxAge);
        return null;
    }

    private static bool IsPublicKeyFor(JsonWebKey key, string algorithm) => key.Kty switch
    {
        JsonWebAlgorithmsKeyTypes.EllipticCurve => string.IsNullOrEmpty(key.D) && !string.IsNullOrEmpty(key.X) && !string.IsNullOrEmpty(key.Y)
            && (algorithm, key.Crv) is (SecurityAlgorithms.EcdsaSha256, JsonWebKeyECTypes.P256) or (SecurityAlgorithms.EcdsaSha384, JsonWebKeyECTypes.P384),
        JsonWebAlgorithmsKeyTypes.RSA => string.IsNullOrEmpty(key.D) && string.IsNullOrEmpty(key.P) && string.IsNullOrEmpty(key.Q)
            && algorithm is SecurityAlgorithms.RsaSha256 or SecurityAlgorithms.RsaSsaPssSha256
            && !string.IsNullOrEmpty(key.E) && ModulusLength(key.N) is >= MinRsaKeyBytes and <= MaxRsaKeyBytes,
        _ => false,
    };

    private static int ModulusLength(string? modulus)
    {
        try
        {
            return string.IsNullOrEmpty(modulus) ? 0 : Base64UrlEncoder.DecodeBytes(modulus).AsSpan().TrimStart((byte)0).Length;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    private static bool IsSignedBy(JsonWebToken token, JsonWebKey key)
    {
        try
        {
            var verifier = Verifiers.CreateForVerifying(key, token.Alg);
            try
            {
                return verifier.Verify(
                    Encoding.ASCII.GetBytes($"{token.EncodedHeader}.{token.EncodedPayload}"),
                    Base64UrlEncoder.DecodeBytes(token.EncodedSignature));
            }
            finally
            {
                Verifiers.ReleaseSignatureProvider(verifier);
            }
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or FormatException or NotSupportedException or SecurityTokenException)
        {
            return false;
        }
    }

    /// <summary>Whether <paramref name="htu"/> is the request's URL, without its query and fragment (RFC 9449, section 4.3).</summary>
    private static bool IsTarget(string? htu, Uri target) =>
        Uri.TryCreate(htu, UriKind.Absolute, out var claimed)
        && Uri.Compare(claimed, target, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.SafeUnescaped, StringComparison.Ordinal) == 0;
}
