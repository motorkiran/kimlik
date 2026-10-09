using System.Text.Json;
using System.Text.Json.Nodes;
using Kimlik.Domain.Common;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Application.Clients;

/// <summary>
/// The public keys a web or service client signs its assertions with (<c>private_key_jwt</c>, RFC 7523), as a JWK Set:
/// public signing keys only, RSA of 2048 bits or more or EC on a NIST curve.
/// </summary>
public static class ClientKeys
{
    public const int MaximumCount = 10;

    private const int RsaMinimumModulusBytes = 2048 / 8;

    private static readonly HashSet<string> Curves = new(StringComparer.Ordinal) { "P-256", "P-384", "P-521" };

    public static Result<JsonWebKeySet> Parse(JsonObject json)
    {
        JsonWebKeySet set;
        try
        {
            set = new JsonWebKeySet(json.ToJsonString());
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return ClientErrors.InvalidKeys;
        }

        return set.Keys.Count is > 0 and <= MaximumCount && set.Keys.All(IsPublicSigningKey) ? set : ClientErrors.InvalidKeys;
    }

    /// <summary>The keys with their public parameters, as clients read them back.</summary>
    public static JsonObject ToJson(JsonWebKeySet set) => new() { ["keys"] = new JsonArray([.. set.Keys.Select(ToJson)]) };

    /// <summary>Whether two key sets hold the same keys, in the same order.</summary>
    public static bool AreSame(JsonObject? current, JsonObject declared) =>
        Parse(declared) is { IsSuccess: true } parsed && JsonNode.DeepEquals(current, ToJson(parsed.Value));

    private static bool IsPublicSigningKey(JsonWebKey key) =>
        key.Use is null or JsonWebKeyUseNames.Sig
        && key.D is null
        && key.Kty switch
        {
            JsonWebAlgorithmsKeyTypes.RSA => DecodedLength(key.N) >= RsaMinimumModulusBytes && !string.IsNullOrEmpty(key.E)
                && key.P is null && key.Q is null && key.DP is null && key.DQ is null && key.QI is null,
            JsonWebAlgorithmsKeyTypes.EllipticCurve => key.Crv is not null && Curves.Contains(key.Crv)
                && !string.IsNullOrEmpty(key.X) && !string.IsNullOrEmpty(key.Y),
            _ => false,
        };

    private static int DecodedLength(string? value)
    {
        try
        {
            return string.IsNullOrEmpty(value) ? 0 : Base64UrlEncoder.DecodeBytes(value).Length;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    private static JsonObject ToJson(JsonWebKey key)
    {
        var json = new JsonObject { [JsonWebKeyParameterNames.Kty] = key.Kty };
        foreach (var (name, value) in new[]
        {
            (JsonWebKeyParameterNames.Use, key.Use),
            (JsonWebKeyParameterNames.Kid, key.Kid),
            (JsonWebKeyParameterNames.Alg, key.Alg),
            (JsonWebKeyParameterNames.N, key.N),
            (JsonWebKeyParameterNames.E, key.E),
            (JsonWebKeyParameterNames.Crv, key.Crv),
            (JsonWebKeyParameterNames.X, key.X),
            (JsonWebKeyParameterNames.Y, key.Y),
        })
        {
            if (!string.IsNullOrEmpty(value))
            {
                json[name] = value;
            }
        }

        return json;
    }
}
