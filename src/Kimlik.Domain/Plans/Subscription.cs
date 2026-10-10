using Kimlik.Domain.Common;

namespace Kimlik.Domain.Plans;

public enum SubscriptionStatus
{
    Trialing,
    Active,

    /// <summary>Still in effect until the end of the current period, then expired.</summary>
    Canceled,

    Expired,
}

/// <summary>Who a subscription belongs to: a user or an organization.</summary>
public readonly record struct Subscriber(Guid? UserId, Guid? OrganizationId)
{
    public static Subscriber User(Guid userId) => new(userId, null);

    public static Subscriber Organization(Guid organizationId) => new(null, organizationId);
}

/// <summary>
/// A user's or an organization's plan over time. Billing systems keep it up to date: they renew the period,
/// change the plan or cancel. A subscriber has at most one current subscription; ended ones remain as history.
/// </summary>
public sealed class Subscription
{
    public const int ExternalReferenceMaxLength = 200;

    /// <summary>The most units of one add-on a subscription takes.</summary>
    public const int MaxAddOnQuantity = 10_000;

    private readonly List<SubscriptionAddOn> _addOns = [];
    private readonly List<SubscriptionFeatureOverride> _featureOverrides = [];

    // Used by EF Core.
    private Subscription()
    {
    }

    public Guid Id { get; private init; }

    public Guid PlanId { get; private set; }

    public Guid? UserId { get; private init; }

    public Guid? OrganizationId { get; private init; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset CurrentPeriodStart { get; private set; }

    /// <summary>When the paid period ends; <see langword="null"/> for a subscription without an end, such as a free plan.</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; private set; }

    public DateTimeOffset? TrialEndsAt { get; private set; }

    public DateTimeOffset? CanceledAt { get; private set; }

    /// <summary>The subscription's ID in the billing system, such as a Stripe subscription ID.</summary>
    public string? ExternalReference { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Subscriber Subscriber => new(UserId, OrganizationId);

    /// <summary>The add-ons the subscription takes, with their quantities.</summary>
    public IReadOnlyCollection<SubscriptionAddOn> AddOns => _addOns;

    /// <summary>Feature values of the subscriber's own, whatever its plan and add-ons say.</summary>
    public IReadOnlyCollection<SubscriptionFeatureOverride> FeatureOverrides => _featureOverrides;

    /// <summary>Whether the subscriber gets more, or other, than its plan gives.</summary>
    public bool HasCustomEntitlements => _addOns.Count > 0 || _featureOverrides.Count > 0;

    /// <summary>When the subscription stops giving its plan, unless it is renewed first.</summary>
    public DateTimeOffset? EndsAt => Status switch
    {
        SubscriptionStatus.Trialing => TrialEndsAt,
        SubscriptionStatus.Active or SubscriptionStatus.Canceled => CurrentPeriodEnd,
        _ => null,
    };

    public static Result<Subscription> Create(
        Subscriber subscriber, Guid planId, DateTimeOffset? trialEndsAt, DateTimeOffset? currentPeriodEnd, string? externalReference, DateTimeOffset now)
    {
        if (subscriber.UserId is null == subscriber.OrganizationId is null)
        {
            throw new ArgumentException("A subscription belongs to either a user or an organization.", nameof(subscriber));
        }

        var subscription = new Subscription
        {
            Id = Guid.CreateVersion7(now),
            PlanId = planId,
            UserId = subscriber.UserId,
            OrganizationId = subscriber.OrganizationId,
            CurrentPeriodStart = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var updated = subscription.Update(trialEndsAt, currentPeriodEnd, externalReference, trialEndsAt > now ? SubscriptionStatus.Trialing : SubscriptionStatus.Active, now);
        return updated.IsSuccess ? subscription : updated.Error;
    }

    public bool IsInEffectAt(DateTimeOffset now) => Status != SubscriptionStatus.Expired && (EndsAt is not { } end || now < end);

    public Result ChangePlan(Guid planId, DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Expired)
        {
            return PlanErrors.SubscriptionEnded;
        }

        PlanId = planId;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Records what the billing system says: the trial, the period and whether the subscription is trialing or
    /// active. Setting a canceled subscription to trialing or active resumes it; keeping its status keeps it canceled.
    /// </summary>
    public Result Update(DateTimeOffset? trialEndsAt, DateTimeOffset? currentPeriodEnd, string? externalReference, SubscriptionStatus status, DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Expired)
        {
            return PlanErrors.SubscriptionEnded;
        }

        if (status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Active) && status != Status)
        {
            return PlanErrors.InvalidSubscriptionStatus;
        }

        if ((status == SubscriptionStatus.Trialing && trialEndsAt is null) || externalReference?.Trim().Length > ExternalReferenceMaxLength)
        {
            return PlanErrors.InvalidSubscription;
        }

        if (currentPeriodEnd is { } end && end <= CurrentPeriodStart)
        {
            return PlanErrors.InvalidSubscription;
        }

        TrialEndsAt = trialEndsAt;
        CurrentPeriodEnd = currentPeriodEnd;
        ExternalReference = string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim();
        if (status != SubscriptionStatus.Canceled)
        {
            CanceledAt = null;
        }

        Status = status;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Cancels at the end of the current period; a subscription without an end expires at once.</summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (Status is SubscriptionStatus.Expired or SubscriptionStatus.Canceled)
        {
            return PlanErrors.SubscriptionEnded;
        }

        CanceledAt = now;
        UpdatedAt = now;
        Status = Status == SubscriptionStatus.Active && CurrentPeriodEnd > now ? SubscriptionStatus.Canceled : SubscriptionStatus.Expired;
        return Result.Success();
    }

    /// <summary>Replaces the add-ons, by the ID of each add-on plan and its quantity.</summary>
    public Result SetAddOns(IReadOnlyDictionary<Guid, int> quantities, DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Expired)
        {
            return PlanErrors.SubscriptionEnded;
        }

        if (quantities.Values.Any(quantity => quantity is < 1 or > MaxAddOnQuantity))
        {
            return PlanErrors.InvalidQuantity;
        }

        _addOns.Clear();
        _addOns.AddRange(quantities.Select(pair => new SubscriptionAddOn(Id, pair.Key, pair.Value)));
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Replaces the feature values the subscriber gets whatever its plan and add-ons say.</summary>
    public Result SetFeatureOverrides(IEnumerable<FeatureSetting> settings, DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Expired)
        {
            return PlanErrors.SubscriptionEnded;
        }

        var wanted = settings.ToList();
        if (wanted.Exists(setting => setting.Limit is < 0) || wanted.DistinctBy(setting => setting.FeatureId).Count() != wanted.Count)
        {
            return PlanErrors.InvalidFeatureValue;
        }

        _featureOverrides.Clear();
        _featureOverrides.AddRange(wanted.Select(setting => new SubscriptionFeatureOverride(Id, setting.FeatureId, setting.Enabled, setting.Limit)));
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Ends a subscription whose trial or period is over.</summary>
    public bool ExpireIfEnded(DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Expired || IsInEffectAt(now))
        {
            return false;
        }

        Status = SubscriptionStatus.Expired;
        UpdatedAt = now;
        return true;
    }
}

/// <summary>An add-on that a subscription takes, and how many units of it.</summary>
public sealed class SubscriptionAddOn(Guid subscriptionId, Guid planId, int quantity)
{
    public Guid SubscriptionId { get; private init; } = subscriptionId;

    /// <summary>The add-on: a plan of the <see cref="PlanKind.AddOn"/> kind.</summary>
    public Guid PlanId { get; private init; } = planId;

    public int Quantity { get; private init; } = quantity;
}

/// <summary>A feature value of one subscription's own, in the form of <see cref="PlanFeature"/>.</summary>
public sealed class SubscriptionFeatureOverride(Guid subscriptionId, Guid featureId, bool enabled, long? limit)
{
    public Guid SubscriptionId { get; private init; } = subscriptionId;

    public Guid FeatureId { get; private init; } = featureId;

    public bool Enabled { get; private init; } = enabled;

    /// <summary>The maximum of a limit; null means unlimited.</summary>
    public long? Limit { get; private init; } = limit;
}
