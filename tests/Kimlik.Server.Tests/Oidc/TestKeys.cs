using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>JWK Sets of keys generated in a test, for clients that authenticate with keys.</summary>
internal static class TestKeys
{
    /// <summary>A JWK Set with the public key of <paramref name="key"/>, or with its private parameters too.</summary>
    public static JsonObject KeySet(AsymmetricAlgorithm key, string keyId, bool includePrivateParameters = false)
    {
        var jwk = key switch
        {
            RSA rsa => JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters))),
            ECDsa ecdsa => PublicJwk(ecdsa),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        var json = new JsonObject { ["kty"] = jwk.Kty, ["kid"] = keyId, ["use"] = "sig" };
        foreach (var (name, value) in new[] { ("n", jwk.N), ("e", jwk.E), ("d", jwk.D), ("crv", jwk.Crv), ("x", jwk.X), ("y", jwk.Y) })
        {
            if (!string.IsNullOrEmpty(value))
            {
                json[name] = value;
            }
        }

        return new JsonObject { ["keys"] = new JsonArray(json) };
    }

    private static JsonWebKey PublicJwk(ECDsa key)
    {
        using var publicKey = ECDsa.Create(key.ExportParameters(includePrivateParameters: false));
        return JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(publicKey));
    }
}
