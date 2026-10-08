using Kimlik.Application.Abstractions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Plans;

/// <summary>
/// Resolves the plan in effect for a user or an organization: the plan of its current subscription while that is in
/// effect, otherwise the configured default plan. It checks the period itself, so it never waits for expiration.
/// </summary>
public sealed class Entitlements(IKimlikDbContext context, IOptions<PlanOptions> options, TimeProvider timeProvider)
{
    public async Task<Plan?> PlanOfAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var current = await context.Subscriptions.AsNoTracking()
            .Where(subscription => subscription.Status != Domain.Plans.SubscriptionStatus.Expired
                && (subscriber.UserId != null ? subscription.UserId == subscriber.UserId : subscription.OrganizationId == subscriber.OrganizationId))
            .SingleOrDefaultAsync(cancellationToken);

        var defaultPlan = subscriber.UserId is not null ? options.Value.DefaultUserPlan : options.Value.DefaultOrganizationPlan;
        var plans = context.Plans.AsNoTracking().Include(plan => plan.Features);

        return current is not null && current.IsInEffectAt(now)
            ? await plans.SingleAsync(plan => plan.Id == current.PlanId, cancellationToken)
            : defaultPlan is null ? null : await plans.SingleOrDefaultAsync(plan => plan.Key == defaultPlan, cancellationToken);
    }

    public async Task<EntitlementsResponse> DescribeAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var plan = await PlanOfAsync(subscriber, cancellationToken);
        var features = await context.AllFeaturesAsync(cancellationToken);
        return plan is null
            ? new EntitlementsResponse(null, FeatureValues.Describe(Plan.None, features))
            : new EntitlementsResponse(plan.Key, FeatureValues.Describe(plan, features));
    }
}
