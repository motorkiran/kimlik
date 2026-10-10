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

/// <summary>
/// A user's or an organization's plan over time, as the billing system reports it, with the add-ons it takes, by key
/// and quantity, and the feature values of its own that override what its plan and add-ons give.
/// </summary>
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
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, int> AddOns,
    IReadOnlyDictionary<string, JsonElement> FeatureOverrides);

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

    /// <summary>Add-ons by key, with the quantity of each, such as <c>{ "extra-seats": 3 }</c>.</summary>
    [MaxLength(50)]
    public IReadOnlyDictionary<string, int> AddOns { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Feature values of the subscriber's own, in the form of plan values, such as <c>{ "max_projects": 50 }</c>; they
    /// win over the plan and the add-ons.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> FeatureOverrides { get; init; } = new Dictionary<string, JsonElement>();
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

    /// <summary>Replaces the add-ons; <c>{}</c> removes them.</summary>
    [MaxLength(50)]
    public IReadOnlyDictionary<string, int>? AddOns { get; init; }

    /// <summary>Replaces the feature overrides; <c>{}</c> removes them.</summary>
    public IReadOnlyDictionary<string, JsonElement>? FeatureOverrides { get; init; }

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
/// plan, and the value of every feature, with the subscription's add-ons and overrides. <c>plan</c> is
/// <see langword="null"/> when there is neither.
/// </summary>
public sealed record EntitlementsResponse(string? Plan, IReadOnlyDictionary<string, JsonElement> Features);
