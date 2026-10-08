using System.Security.Cryptography;

namespace Kimlik.Application.Webhooks;

/// <summary>
/// Signing secrets in the Standard Webhooks format: <c>whsec_</c> and 256 random bits in base64, which are the HMAC
/// key. They are stored encrypted, because Kimlik needs them to sign.
/// </summary>
public static class WebhookSecrets
{
    public const string Prefix = "whsec_";

    /// <summary>The purpose the secrets are encrypted for.</summary>
    public const string Purpose = "webhooks.endpoint-secret";

    public static string Generate() => Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>The HMAC key in a secret.</summary>
    public static byte[] KeyOf(string secret) => Convert.FromBase64String(secret.StartsWith(Prefix, StringComparison.Ordinal) ? secret[Prefix.Length..] : secret);
}
