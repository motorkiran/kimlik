using System.Text.Json;
using Kimlik.Application.Abstractions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Plans;

internal static class UsageMapping
{
    public static UsageResponse ToUsage(string feature, long used, long? limit, DateOnly period)
    {
        var start = new DateTimeOffset(period.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return new UsageResponse(feature, used, limit, limit is { } max ? Math.Max(0, max - used) : null, start, start.AddMonths(1));
    }

    /// <summary>The month's limit of each metered feature for the subscriber, by key; <see langword="null"/> is unlimited.</summary>
    public static async Task<Dictionary<string, long?>> LimitsAsync(this Entitlements entitlements, Subscriber subscriber, CancellationToken cancellationToken) =>
        (await entitlements.DescribeAsync(subscriber, cancellationToken)).Features
            .Where(feature => feature.Value.ValueKind is JsonValueKind.Number or JsonValueKind.Null)
            .ToDictionary(feature => feature.Key, feature => feature.Value.ValueKind == JsonValueKind.Number ? feature.Value.GetInt64() : (long?)null, StringComparer.Ordinal);

    public static Task<bool> ExistsAsync(this IKimlikDbContext context, Subscriber subscriber, CancellationToken cancellationToken) =>
        subscriber.UserId is { } userId
            ? context.Users.AnyAsync(user => user.Id == userId, cancellationToken)
            : context.Organizations.AnyAsync(organization => organization.Id == subscriber.OrganizationId, cancellationToken);
}

/// <summary>
/// Records use of a metered limit, as a backend reports it: with <c>enforce</c>, only within the month's limit, and with
/// an idempotency key, once however often it is retried within a day.
/// </summary>
public sealed class RecordUsageHandler(IKimlikDbContext context, Entitlements entitlements, IUsageCounters counters, TimeProvider timeProvider)
{
    public const long MaxQuantity = 1_000_000_000;

    public async Task<Result<UsageResponse>> HandleAsync(RecordUsageRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity is < 1 or > MaxQuantity || request.IdempotencyKey is { Length: 0 or > UsageRecord.KeyMaxLength })
        {
            return PlanErrors.InvalidUsage;
        }

        var subscriber = SubscriptionMapping.ToSubscriber(request.SubscriberType, request.SubscriberId);
        if (!await context.ExistsAsync(subscriber, cancellationToken))
        {
            return PlanErrors.SubscriberNotFound;
        }

        if (await context.Features.AsNoTracking().SingleOrDefaultAsync(feature => feature.Key == request.Feature, cancellationToken) is not { } feature)
        {
            return PlanErrors.FeatureNotFound;
        }

        if (!feature.IsMetered)
        {
            return PlanErrors.NotMetered;
        }

        var limit = (await entitlements.LimitsAsync(subscriber, cancellationToken)).GetValueOrDefault(feature.Key);
        var now = timeProvider.GetUtcNow();
        var period = UsageCounter.PeriodOf(now);
        if (request.Enforce && request.Quantity > limit)
        {
            return PlanErrors.LimitReached;
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        if (request.IdempotencyKey is { } key && !await counters.ClaimAsync(subscriber, key, now, cancellationToken))
        {
            // A retry of use already counted: the answer is the month's use as it is now.
            var counted = await context.UsageCounters.AsNoTracking()
                .Where(counter => counter.FeatureId == feature.Id && counter.Period == period
                    && (subscriber.UserId != null ? counter.UserId == subscriber.UserId : counter.OrganizationId == subscriber.OrganizationId))
                .Select(counter => counter.Used)
                .SingleOrDefaultAsync(cancellationToken);
            return UsageMapping.ToUsage(feature.Key, counted, limit, period);
        }

        if (await counters.AddAsync(subscriber, feature.Id, period, request.Quantity, request.Enforce ? limit : null, now, cancellationToken) is not { } used)
        {
            return PlanErrors.LimitReached;
        }

        await transaction.CommitAsync(cancellationToken);
        return UsageMapping.ToUsage(feature.Key, used, limit, period);
    }
}

/// <summary>A subscriber's use of every metered limit this month, with its limit.</summary>
public sealed class GetUsageHandler(IKimlikDbContext context, Entitlements entitlements, TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<UsageResponse>>> HandleAsync(SubscriberType subscriberType, Guid subscriberId, CancellationToken cancellationToken)
    {
        var subscriber = SubscriptionMapping.ToSubscriber(subscriberType, subscriberId);
        if (!await context.ExistsAsync(subscriber, cancellationToken))
        {
            return PlanErrors.SubscriberNotFound;
        }

        var period = UsageCounter.PeriodOf(timeProvider.GetUtcNow());
        var metered = await context.Features.AsNoTracking().Where(feature => feature.IsMetered).OrderBy(feature => feature.Key).ToListAsync(cancellationToken);
        var used = await context.UsageCounters.AsNoTracking()
            .Where(counter => counter.Period == period
                && (subscriber.UserId != null ? counter.UserId == subscriber.UserId : counter.OrganizationId == subscriber.OrganizationId))
            .ToDictionaryAsync(counter => counter.FeatureId, counter => counter.Used, cancellationToken);
        var limits = await entitlements.LimitsAsync(subscriber, cancellationToken);

        return metered.Select(feature => UsageMapping.ToUsage(feature.Key, used.GetValueOrDefault(feature.Id), limits.GetValueOrDefault(feature.Key), period)).ToList();
    }
}
