using System.Net;
using Kimlik.Domain.Common;

namespace Kimlik.Domain.Webhooks;

/// <summary>
/// A URL in the application that receives events, signed with a secret of its own. It receives the event types it
/// subscribes to, or every type when it lists none.
/// </summary>
public sealed class WebhookEndpoint
{
    public const int UrlMaxLength = 2048;
    public const int DescriptionMaxLength = 256;

    // Used by EF Core.
    private WebhookEndpoint()
    {
    }

    public Guid Id { get; private init; }

    public string Url { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public List<string> EventTypes { get; private set; } = [];

    /// <summary>The signing secret, encrypted with the master key.</summary>
    public string EncryptedSecret { get; private set; } = string.Empty;

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When deliveries started failing without a success since; flags an endpoint that needs attention.</summary>
    public DateTimeOffset? FailingSince { get; private set; }

    /// <summary>A new endpoint; the caller checks that the event types exist, and none means all.</summary>
    public static Result<WebhookEndpoint> Create(string url, string? description, IEnumerable<string> eventTypes, bool enabled, DateTimeOffset now)
    {
        var endpoint = new WebhookEndpoint { Id = Guid.CreateVersion7(now), CreatedAt = now, Enabled = enabled };
        var updated = endpoint.Update(url, description, eventTypes, enabled, now);
        return updated.IsSuccess ? endpoint : updated.Error;
    }

    public Result Update(string url, string? description, IEnumerable<string> eventTypes, bool enabled, DateTimeOffset now)
    {
        if (!IsValidUrl(url))
        {
            return WebhookErrors.InvalidUrl;
        }

        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description?.Length > DescriptionMaxLength)
        {
            return WebhookErrors.InvalidDescription;
        }

        Url = url;
        Description = description;
        EventTypes = [.. eventTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (Enabled != enabled)
        {
            Enabled = enabled;
            FailingSince = null;
        }

        UpdatedAt = now;
        return Result.Success();
    }

    public void SetSecret(string encryptedSecret, DateTimeOffset now)
    {
        EncryptedSecret = encryptedSecret;
        UpdatedAt = now;
    }

    public bool Receives(string eventType) => Enabled && (EventTypes.Count == 0 || EventTypes.Contains(eventType));

    public void RecordSuccess() => FailingSince = null;

    public void RecordFailure(DateTimeOffset now) => FailingSince ??= now;

    /// <summary>
    /// An absolute HTTPS URL without credentials in it. Plain HTTP is accepted for the local machine only, for
    /// development.
    /// </summary>
    public static bool IsValidUrl(string? url) =>
        url is { Length: <= UrlMaxLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IsLocal(uri)));

    private static bool IsLocal(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
}
