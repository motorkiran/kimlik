using System.ComponentModel.DataAnnotations;

namespace Kimlik.Contracts.Management;

/// <summary>Use of a metered limit, as a backend reports it.</summary>
public sealed record RecordUsageRequest
{
    public required SubscriberType SubscriberType { get; init; }

    public required Guid SubscriberId { get; init; }

    /// <summary>The key of a metered limit feature.</summary>
    [Required]
    [StringLength(64)]
    public required string Feature { get; init; }

    [Range(1, 1_000_000_000)]
    public long Quantity { get; init; } = 1;

    /// <summary>Record the use only if the month's use stays within the limit; otherwise <c>usage.limit_reached</c>.</summary>
    public bool Enforce { get; init; }

    /// <summary>A key of the backend's choosing that makes a retry within a day count once, such as a request ID.</summary>
    [StringLength(100)]
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// A subscriber's use of a metered limit in a calendar month, in UTC: <c>limit</c> and <c>remaining</c> are
/// <see langword="null"/> when it is unlimited.
/// </summary>
public sealed record UsageResponse(string Feature, long Used, long? Limit, long? Remaining, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd);
