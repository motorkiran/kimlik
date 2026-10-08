namespace Kimlik.Infrastructure.Webhooks;

/// <summary>Delivery settings from the <c>Kimlik:Webhooks</c> configuration section.</summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Kimlik:Webhooks";

    /// <summary>How often due deliveries are looked for; new events on this instance are sent at once.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Lets endpoints on the local machine and private networks receive webhooks, as in development or when Kimlik
    /// and the application share an internal network. Off, webhooks only go to the public internet.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>How long an endpoint has to answer an attempt.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The waits before each retry of a failed delivery, after which it fails for good. By default, Kimlik retries for
    /// about 21 hours: after 10 seconds, 1 and 5 minutes, half an hour, 2, 6 and 12 hours.
    /// </summary>
    public TimeSpan[]? RetryDelays { get; set; }

    public int BatchSize { get; set; } = 20;

    /// <summary>How long the delivery log is kept.</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    private static readonly TimeSpan[] DefaultRetryDelays =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
    ];

    /// <summary>The configured delays, or the defaults; configuration would add to a default list rather than replace it.</summary>
    internal IReadOnlyList<TimeSpan> Delays => RetryDelays is { Length: > 0 } ? RetryDelays : DefaultRetryDelays;

    internal bool IsValid() =>
        PollingInterval > TimeSpan.Zero
        && Timeout > TimeSpan.Zero
        && Delays.All(delay => delay > TimeSpan.Zero)
        && BatchSize is > 0 and <= 100
        && RetentionPeriod > TimeSpan.Zero;
}
