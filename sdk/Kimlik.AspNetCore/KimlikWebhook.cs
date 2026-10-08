using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimlik.Contracts.Webhooks;
using Microsoft.AspNetCore.Http;

namespace Kimlik.AspNetCore;

/// <summary>
/// Checks that a webhook comes from Kimlik, as the Standard Webhooks specification describes: its signature must
/// match the endpoint's secret, and its timestamp must be recent, which turns replayed requests away. Delivery is at
/// least once, so handle each <c>webhook-id</c> once.
/// </summary>
public static class KimlikWebhook
{
    /// <summary>How far the timestamp may be from the current time.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private const string SecretPrefix = "whsec_";
    private const string SignatureVersion = "v1,";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads the webhook in the request, or returns <see langword="null"/> if it is not one from Kimlik signed with
    /// <paramref name="secret"/>, the endpoint's <c>whsec_…</c> secret.
    /// </summary>
    public static async Task<WebhookEvent?> ReadAsync(HttpRequest request, string secret, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        return TryVerify(request.Headers, body, secret, out var webhookEvent) ? webhookEvent : null;
    }

    /// <summary>Whether the headers and body make a webhook from Kimlik signed with <paramref name="secret"/>.</summary>
    public static bool TryVerify(IHeaderDictionary headers, string body, string secret, [NotNullWhen(true)] out WebhookEvent? webhookEvent) =>
        TryVerify(headers, body, secret, TimeProvider.System, out webhookEvent);

    /// <inheritdoc cref="TryVerify(IHeaderDictionary, string, string, out WebhookEvent?)"/>
    public static bool TryVerify(
        IHeaderDictionary headers, string body, string secret, TimeProvider timeProvider, [NotNullWhen(true)] out WebhookEvent? webhookEvent)
    {
        webhookEvent = null;
        string? id = headers["webhook-id"];
        string? timestamp = headers["webhook-timestamp"];
        string? signatures = headers["webhook-signature"];

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(signatures)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || (timeProvider.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > Tolerance)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(KeyOf(secret), Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));

        // The header can carry several signatures, such as while a secret is being replaced.
        var signed = signatures.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(signature =>
            signature.StartsWith(SignatureVersion, StringComparison.Ordinal)
            && TryDecode(signature[SignatureVersion.Length..], out var candidate)
            && CryptographicOperations.FixedTimeEquals(candidate, expected));

        if (!signed)
        {
            return false;
        }

        webhookEvent = JsonSerializer.Deserialize<WebhookEvent>(body, SerializerOptions);
        return webhookEvent is not null;
    }

    private static byte[] KeyOf(string secret) =>
        Convert.FromBase64String(secret.StartsWith(SecretPrefix, StringComparison.Ordinal) ? secret[SecretPrefix.Length..] : secret);

    private static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = new byte[value.Length];
        if (Convert.TryFromBase64String(value, bytes, out var written))
        {
            bytes = bytes[..written];
            return true;
        }

        return false;
    }
}
