using System.Text.Json;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using DomainStatus = Kimlik.Domain.Plans.SubscriptionStatus;
using PlanKind = Kimlik.Domain.Plans.PlanKind;
using SubscriptionStatus = Kimlik.Contracts.Management.SubscriptionStatus;

namespace Kimlik.Application.Plans;

internal static class SubscriptionMapping
{
    public static async Task<List<SubscriptionResponse>> ToResponsesAsync(
        this IKimlikDbContext context, IReadOnlyCollection<Subscription> subscriptions, CancellationToken cancellationToken)
    {
        var planIds = subscriptions.SelectMany(subscription => subscription.AddOns.Select(addOn => addOn.PlanId).Append(subscription.PlanId)).Distinct().ToList();
        var plans = await context.Plans.Where(plan => planIds.Contains(plan.Id)).ToDictionaryAsync(plan => plan.Id, plan => plan.Key, cancellationToken);
        var features = subscriptions.Any(subscription => subscription.FeatureOverrides.Count > 0) ? await context.AllFeaturesAsync(cancellationToken) : [];

        return [.. subscriptions.Select(subscription => new SubscriptionResponse(
            subscription.Id,
            subscription.UserId is not null ? SubscriberType.User : SubscriberType.Organization,
            subscription.UserId ?? subscription.OrganizationId!.Value,
            plans[subscription.PlanId],
            Enum.Parse<SubscriptionStatus>(subscription.Status.ToString()),
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd,
            subscription.TrialEndsAt,
            subscription.CanceledAt,
            subscription.ExternalReference,
            subscription.CreatedAt,
            subscription.UpdatedAt,
            subscription.AddOns.ToDictionary(addOn => plans[addOn.PlanId], addOn => addOn.Quantity, StringComparer.Ordinal),
            FeatureValues.Describe(subscription.FeatureOverrides, features)))];
    }

    public static async Task<SubscriptionResponse> ToResponseAsync(this IKimlikDbContext context, Subscription subscription, CancellationToken cancellationToken) =>
        (await context.ToResponsesAsync([subscription], cancellationToken))[0];

    /// <summary>Subscriptions with their add-ons and feature overrides.</summary>
    public static IQueryable<Subscription> WithExtras(this IQueryable<Subscription> subscriptions) =>
        subscriptions.Include(subscription => subscription.AddOns).Include(subscription => subscription.FeatureOverrides);

    /// <summary>
    /// Replaces the add-ons, by key and quantity, and the feature overrides of the subscription, those that are given.
    /// Only add-ons go with a subscription, and an archived one only if the subscription has it already.
    /// </summary>
    public static async Task<Result> SetExtrasAsync(
        this IKimlikDbContext context,
        Subscription subscription,
        IReadOnlyDictionary<string, int>? addOns,
        IReadOnlyDictionary<string, JsonElement>? featureOverrides,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (addOns is not null)
        {
            var keys = addOns.Keys.ToList();
            var found = await context.Plans.Where(plan => keys.Contains(plan.Key)).ToDictionaryAsync(plan => plan.Key, StringComparer.Ordinal, cancellationToken);
            if (found.Count != keys.Count)
            {
                return PlanErrors.PlanNotFound;
            }

            if (found.Values.Any(plan => plan.Kind != PlanKind.AddOn))
            {
                return PlanErrors.NotAnAddOn;
            }

            if (found.Values.Any(plan => plan.IsArchived && subscription.AddOns.All(taken => taken.PlanId != plan.Id)))
            {
                return PlanErrors.PlanArchived;
            }

            var set = subscription.SetAddOns(addOns.ToDictionary(pair => found[pair.Key].Id, pair => pair.Value), now);
            if (set.IsFailure)
            {
                return set;
            }
        }

        if (featureOverrides is not null)
        {
            var settings = FeatureValues.Parse(featureOverrides, await context.FeaturesByKeyAsync(cancellationToken));
            if (settings.IsFailure)
            {
                return settings.Error;
            }

            var set = subscription.SetFeatureOverrides(settings.Value, now);
            if (set.IsFailure)
            {
                return set;
            }
        }

        return Result.Success();
    }

    public static Subscriber ToSubscriber(SubscriberType type, Guid id) =>
        type == SubscriberType.User ? Subscriber.User(id) : Subscriber.Organization(id);

    public static void Audit(this IAuditLog auditLog, string action, Subscription subscription, IReadOnlyDictionary<string, object?>? data = null) =>
        auditLog.Record(action, AuditSubject.Subscription(subscription.Id), data, organizationId: subscription.OrganizationId);
}

/// <summary>Lists subscriptions newest first, including ended ones, optionally of one subscriber.</summary>
public sealed record ListSubscriptionsQuery(SubscriberType? SubscriberType, Guid? SubscriberId, string? Cursor, int? Limit);

public sealed class ListSubscriptionsHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<SubscriptionResponse>>> HandleAsync(ListSubscriptionsQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var before))
        {
            return CommonErrors.InvalidCursor;
        }

        if ((query.SubscriberType is null) != (query.SubscriberId is null))
        {
            return CommonErrors.InvalidParameter("subscriberType");
        }

        var subscriptions = context.Subscriptions.WithExtras().AsNoTracking();
        if (before is { } beforeId)
        {
            subscriptions = subscriptions.Where(subscription => subscription.Id < beforeId);
        }

        if (query.SubscriberId is { } subscriberId)
        {
            subscriptions = query.SubscriberType == SubscriberType.User
                ? subscriptions.Where(subscription => subscription.UserId == subscriberId)
                : subscriptions.Where(subscription => subscription.OrganizationId == subscriberId);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(
            subscriptions.OrderByDescending(subscription => subscription.Id), query.Limit, subscription => subscription.Id, cancellationToken);

        return new Page<SubscriptionResponse>(await context.ToResponsesAsync(page, cancellationToken), nextCursor);
    }
}

public sealed class GetSubscriptionHandler(IKimlikDbContext context)
{
    public async Task<Result<SubscriptionResponse>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Subscriptions.WithExtras().AsNoTracking().SingleOrDefaultAsync(subscription => subscription.Id == id, cancellationToken) is { } subscription
            ? await context.ToResponseAsync(subscription, cancellationToken)
            : PlanErrors.SubscriptionNotFound;
}

/// <summary>Subscribes a user or an organization to a plan, as a billing system does after a purchase.</summary>
public sealed class CreateSubscriptionHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<SubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var subscriber = SubscriptionMapping.ToSubscriber(request.SubscriberType, request.SubscriberId);
        var exists = subscriber.UserId is { } userId
            ? await context.Users.AnyAsync(user => user.Id == userId, cancellationToken)
            : await context.Organizations.AnyAsync(organization => organization.Id == subscriber.OrganizationId, cancellationToken);
        if (!exists)
        {
            return PlanErrors.SubscriberNotFound;
        }

        if (await context.Plans.SingleOrDefaultAsync(plan => plan.Key == request.Plan, cancellationToken) is not { } plan)
        {
            return PlanErrors.PlanNotFound;
        }

        if (plan.Kind != PlanKind.Base)
        {
            return PlanErrors.NotABasePlan;
        }

        if (plan.IsArchived)
        {
            return PlanErrors.PlanArchived;
        }

        var now = timeProvider.GetUtcNow();
        var current = await context.Subscriptions.SingleOrDefaultAsync(
            subscription => subscription.Status != DomainStatus.Expired
                && (subscriber.UserId != null ? subscription.UserId == subscriber.UserId : subscription.OrganizationId == subscriber.OrganizationId),
            cancellationToken);

        if (current is not null)
        {
            // One that ended but has not been marked yet makes way for the new one.
            if (!current.ExpireIfEnded(now))
            {
                return PlanErrors.SubscriptionExists;
            }

            auditLog.Audit(AuditActions.SubscriptionExpired, current);
            await context.SaveChangesAsync(cancellationToken);
        }

        var created = Subscription.Create(subscriber, plan.Id, request.TrialEndsAt, request.CurrentPeriodEnd, request.ExternalReference, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var extras = await context.SetExtrasAsync(created.Value, request.AddOns, request.FeatureOverrides, now, cancellationToken);
        if (extras.IsFailure)
        {
            return extras.Error;
        }

        context.Subscriptions.Add(created.Value);
        auditLog.Audit(AuditActions.SubscriptionCreated, created.Value, new Dictionary<string, object?> { ["plan"] = plan.Key });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return PlanErrors.SubscriptionExists;
        }

        return await context.ToResponseAsync(created.Value, cancellationToken);
    }
}

/// <summary>Changes the plan, period, trial, status or external reference of a subscription.</summary>
public sealed class UpdateSubscriptionHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<SubscriptionResponse>> HandleAsync(Guid id, UpdateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (await context.Subscriptions.WithExtras().SingleOrDefaultAsync(subscription => subscription.Id == id, cancellationToken) is not { } subscription)
        {
            return PlanErrors.SubscriptionNotFound;
        }

        var now = timeProvider.GetUtcNow();
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (request.Plan is { } planKey)
        {
            if (await context.Plans.SingleOrDefaultAsync(plan => plan.Key == planKey, cancellationToken) is not { } plan)
            {
                return PlanErrors.PlanNotFound;
            }

            if (plan.Kind != PlanKind.Base)
            {
                return PlanErrors.NotABasePlan;
            }

            if (plan.Id != subscription.PlanId)
            {
                if (plan.IsArchived)
                {
                    return PlanErrors.PlanArchived;
                }

                var changed = subscription.ChangePlan(plan.Id, now);
                if (changed.IsFailure)
                {
                    return changed.Error;
                }

                data["plan"] = plan.Key;
            }
        }

        var updated = subscription.Update(
            request.HasTrialEndsAt ? request.TrialEndsAt : subscription.TrialEndsAt,
            request.HasCurrentPeriodEnd ? request.CurrentPeriodEnd : subscription.CurrentPeriodEnd,
            request.HasExternalReference ? request.ExternalReference : subscription.ExternalReference,
            request.Status is { } status ? Enum.Parse<DomainStatus>(status.ToString()) : subscription.Status,
            now);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var extras = await context.SetExtrasAsync(subscription, request.AddOns, request.FeatureOverrides, now, cancellationToken);
        if (extras.IsFailure)
        {
            return extras.Error;
        }

        if (request.AddOns is not null)
        {
            data["add_ons"] = request.AddOns;
        }

        if (request.FeatureOverrides is not null)
        {
            data["feature_overrides"] = request.FeatureOverrides.Keys.Order(StringComparer.Ordinal).ToArray();
        }

        auditLog.Audit(AuditActions.SubscriptionUpdated, subscription, data.Count > 0 ? data : null);
        await context.SaveChangesAsync(cancellationToken);
        return await context.ToResponseAsync(subscription, cancellationToken);
    }
}

/// <summary>Cancels a subscription at the end of its period; one without an end expires at once.</summary>
public sealed class CancelSubscriptionHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<SubscriptionResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await context.Subscriptions.WithExtras().SingleOrDefaultAsync(subscription => subscription.Id == id, cancellationToken) is not { } subscription)
        {
            return PlanErrors.SubscriptionNotFound;
        }

        var canceled = subscription.Cancel(timeProvider.GetUtcNow());
        if (canceled.IsFailure)
        {
            return canceled.Error;
        }

        auditLog.Audit(AuditActions.SubscriptionCanceled, subscription);
        await context.SaveChangesAsync(cancellationToken);
        return await context.ToResponseAsync(subscription, cancellationToken);
    }
}

/// <summary>Marks a batch of subscriptions whose trial or period is over as expired.</summary>
public sealed class ExpireSubscriptionsHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public const int BatchSize = 100;

    /// <returns>How many subscriptions expired; a full batch means there may be more.</returns>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var ended = await context.Subscriptions
            .Where(subscription =>
                (subscription.Status == DomainStatus.Trialing && subscription.TrialEndsAt <= now)
                || ((subscription.Status == DomainStatus.Active || subscription.Status == DomainStatus.Canceled) && subscription.CurrentPeriodEnd <= now))
            .OrderBy(subscription => subscription.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var subscription in ended.Where(subscription => subscription.ExpireIfEnded(now)))
        {
            auditLog.Audit(AuditActions.SubscriptionExpired, subscription);
        }

        await context.SaveChangesAsync(cancellationToken);
        return ended.Count;
    }
}
