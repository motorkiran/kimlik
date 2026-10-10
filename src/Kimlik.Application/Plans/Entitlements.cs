using Kimlik.Application.Abstractions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlanKind = Kimlik.Domain.Plans.PlanKind;

namespace Kimlik.Application.Plans;

/// <summary>
/// What a subscriber's entitlements come from: the plan in effect, and the current subscription with its add-ons and
/// overrides when the plan is its.
/// </summary>
public sealed record EntitlementSource(Plan? Plan, Subscription? Subscription)
{
    /// <summary>Whether the subscriber gets more, or other, than its plan gives.</summary>
    public bool IsCustom => Subscription?.HasCustomEntitlements == true;
}

/// <summary>
/// Resolves what a user or an organization is entitled to: the plan of its current subscription while that is in
/// effect, with the subscription's add-ons and overrides, otherwise the configured default plan. It checks the period
/// itself, so it never waits for expiration.
/// </summary>
public sealed class Entitlements(IKimlikDbContext context, IOptions<PlanOptions> options, TimeProvider timeProvider)
{
    public async Task<EntitlementSource> SourceOfAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var current = await context.Subscriptions.AsNoTracking()
            .Include(subscription => subscription.AddOns)
            .Include(subscription => subscription.FeatureOverrides)
            .Where(subscription => subscription.Status != Domain.Plans.SubscriptionStatus.Expired
                && (subscriber.UserId != null ? subscription.UserId == subscriber.UserId : subscription.OrganizationId == subscriber.OrganizationId))
            .SingleOrDefaultAsync(cancellationToken);

        var plans = context.Plans.AsNoTracking().Include(plan => plan.Features);
        if (current is not null && current.IsInEffectAt(now))
        {
            return new EntitlementSource(await plans.SingleAsync(plan => plan.Id == current.PlanId, cancellationToken), current);
        }

        var defaultPlan = subscriber.UserId is not null ? options.Value.DefaultUserPlan : options.Value.DefaultOrganizationPlan;
        return new EntitlementSource(
            defaultPlan is null ? null : await plans.SingleOrDefaultAsync(plan => plan.Key == defaultPlan && plan.Kind == PlanKind.Base, cancellationToken),
            Subscription: null);
    }

    public async Task<EntitlementsResponse> DescribeAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var source = await SourceOfAsync(subscriber, cancellationToken);
        var features = await context.AllFeaturesAsync(cancellationToken);
        if (source.Plan is null)
        {
            return new EntitlementsResponse(null, FeatureValues.Describe(Plan.None, features));
        }

        var taken = source.Subscription?.AddOns.ToDictionary(addOn => addOn.PlanId, addOn => addOn.Quantity) ?? [];
        var addOns = taken.Count == 0
            ? []
            : await context.Plans.AsNoTracking().Include(plan => plan.Features).Where(plan => taken.Keys.Contains(plan.Id)).ToListAsync(cancellationToken);

        return new EntitlementsResponse(
            source.Plan.Key,
            FeatureValues.Describe(source.Plan, [.. addOns.Select(addOn => (addOn, taken[addOn.Id]))], source.Subscription?.FeatureOverrides ?? [], features));
    }
}
