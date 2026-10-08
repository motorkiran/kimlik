using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

public enum SubscriberType
{
    User,
    Organization,
}

public enum SubscriptionStatus
{
    Trialing,
    Active,

    /// <summary>Still in effect until the end of the current period, then expired.</summary>
    Canceled,

    Expired,
}

/// <summary>A user's or an organization's plan over time, as the billing system reports it.</summary>
public sealed record SubscriptionResponse(
    Guid Id,
    SubscriberType SubscriberType,
    Guid SubscriberId,
    string Plan,
    SubscriptionStatus Status,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? CanceledAt,
    string? ExternalReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateSubscriptionRequest
{
    public required SubscriberType SubscriberType { get; init; }

    public required Guid SubscriberId { get; init; }

    /// <summary>The plan key.</summary>
    [Required]
    [StringLength(64)]
    public required string Plan { get; init; }

    /// <summary>When a trial ends; the subscription is trialing until then.</summary>
    public DateTimeOffset? TrialEndsAt { get; init; }

    /// <summary>When the paid period ends; omit it for a subscription without an end.</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; init; }

    /// <summary>The subscription's ID in the billing system.</summary>
    [StringLength(200)]
    public string? ExternalReference { get; init; }
}

/// <summary>
/// Changes a subscription with JSON Merge Patch semantics: an omitted property keeps its value and <c>null</c> clears
/// it. Setting <c>status</c> to trialing or active resumes a canceled subscription.
/// </summary>
public sealed record UpdateSubscriptionRequest
{
    /// <summary>A plan key, to change the plan.</summary>
    [StringLength(64)]
    public string? Plan { get; init; }

    public SubscriptionStatus? Status { get; init; }

    public DateTimeOffset? TrialEndsAt
    {
        get;
        init
        {
            field = value;
            HasTrialEndsAt = true;
        }
    }

    public DateTimeOffset? CurrentPeriodEnd
    {
        get;
        init
        {
            field = value;
            HasCurrentPeriodEnd = true;
        }
    }

    [StringLength(200)]
    public string? ExternalReference
    {
        get;
        init
        {
            field = value;
            HasExternalReference = true;
        }
    }

    /// <summary>Whether the request sets <see cref="TrialEndsAt"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasTrialEndsAt { get; private init; }

    /// <summary>Whether the request sets <see cref="CurrentPeriodEnd"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasCurrentPeriodEnd { get; private init; }

    /// <summary>Whether the request sets <see cref="ExternalReference"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasExternalReference { get; private init; }
}

/// <summary>
/// What a user or an organization is entitled to: the plan in effect, from its current subscription or the default
/// plan, and the value of every feature. <c>plan</c> is <see langword="null"/> when there is neither.
/// </summary>
public sealed record EntitlementsResponse(string? Plan, IReadOnlyDictionary<string, JsonElement> Features);
