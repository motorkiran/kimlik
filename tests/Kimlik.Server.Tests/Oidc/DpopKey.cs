using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>A client's DPoP key, which makes proofs (RFC 9449) as a client would, or as one should not.</summary>
internal sealed class DpopKey : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The key's JWK SHA-256 thumbprint (RFC 7638), which bound tokens carry as <c>cnf.jkt</c>.</summary>
    public string Thumbprint =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes($$"""{"crv":"P-256","kty":"EC","x":"{{X}}","y":"{{Y}}"}""")));

    private string X => Base64UrlEncoder.Encode(_key.ExportParameters(false).Q.X);

    private string Y => Base64UrlEncoder.Encode(_key.ExportParameters(false).Q.Y);

    /// <summary>A proof for a <paramref name="method"/> request to <paramref name="path"/> at the test server, with <paramref name="accessToken"/>.</summary>
    public string Proof(
        string method,
        string path,
        string? accessToken = null,
        DateTimeOffset? issuedAt = null,
        string type = "dpop+jwt",
        Action<JsonObject>? header = null,
        Action<JsonObject>? payload = null)
    {
        var headerJson = new JsonObject
        {
            ["typ"] = type,
            ["alg"] = "ES256",
            ["jwk"] = new JsonObject { ["kty"] = "EC", ["crv"] = "P-256", ["x"] = X, ["y"] = Y },
        };
        header?.Invoke(headerJson);

        var payloadJson = new JsonObject
        {
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["htm"] = method,
            ["htu"] = TestConfiguration.PublicUrl.TrimEnd('/') + path,
            ["iat"] = (issuedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
        };
        if (accessToken is not null)
        {
            payloadJson["ath"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
        }

        payload?.Invoke(payloadJson);

        var signingInput = $"{Encode(headerJson)}.{Encode(payloadJson)}";
        var signature = _key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return $"{signingInput}.{Base64UrlEncoder.Encode(signature)}";
    }

    /// <summary>The private parameter of the key, which a proof must never carry.</summary>
    public string PrivateParameter => Base64UrlEncoder.Encode(_key.ExportParameters(true).D);

    public void Dispose() => _key.Dispose();

    private static string Encode(JsonObject json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json.ToJsonString(JsonSerializerOptions.Default)));
}
