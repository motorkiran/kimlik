using Kimlik.Application.Plans;
using Kimlik.Domain.Plans;
using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Infrastructure.Plans;

/// <summary>
/// Counts use with one upsert per request, so that concurrent requests never lose a count nor, with a limit, go past
/// it: the row is locked while its sum is checked and raised.
/// </summary>
internal sealed class UsageCounters(KimlikDbContext context) : IUsageCounters
{
    public async Task<long?> AddAsync(
        Subscriber subscriber, Guid featureId, DateOnly period, long quantity, long? limit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7(now);
        var used = (subscriber.UserId, limit) switch
        {
            ({ } userId, { } max) => context.Database.SqlQuery<long>($"""
                INSERT INTO kimlik.usage_counters (id, user_id, feature_id, period, used, updated_at) VALUES ({id}, {userId}, {featureId}, {period}, {quantity}, {now})
                ON CONFLICT (user_id, feature_id, period) WHERE user_id IS NOT NULL
                DO UPDATE SET used = usage_counters.used + excluded.used, updated_at = excluded.updated_at WHERE usage_counters.used + excluded.used <= {max}
                RETURNING used AS "Value"
                """),
            ({ } userId, null) => context.Database.SqlQuery<long>($"""
                INSERT INTO kimlik.usage_counters (id, user_id, feature_id, period, used, updated_at) VALUES ({id}, {userId}, {featureId}, {period}, {quantity}, {now})
                ON CONFLICT (user_id, feature_id, period) WHERE user_id IS NOT NULL
                DO UPDATE SET used = usage_counters.used + excluded.used, updated_at = excluded.updated_at
                RETURNING used AS "Value"
                """),
            (null, { } max) => context.Database.SqlQuery<long>($"""
                INSERT INTO kimlik.usage_counters (id, organization_id, feature_id, period, used, updated_at) VALUES ({id}, {subscriber.OrganizationId}, {featureId}, {period}, {quantity}, {now})
                ON CONFLICT (organization_id, feature_id, period) WHERE organization_id IS NOT NULL
                DO UPDATE SET used = usage_counters.used + excluded.used, updated_at = excluded.updated_at WHERE usage_counters.used + excluded.used <= {max}
                RETURNING used AS "Value"
                """),
            (null, null) => context.Database.SqlQuery<long>($"""
                INSERT INTO kimlik.usage_counters (id, organization_id, feature_id, period, used, updated_at) VALUES ({id}, {subscriber.OrganizationId}, {featureId}, {period}, {quantity}, {now})
                ON CONFLICT (organization_id, feature_id, period) WHERE organization_id IS NOT NULL
                DO UPDATE SET used = usage_counters.used + excluded.used, updated_at = excluded.updated_at
                RETURNING used AS "Value"
                """),
        };

        var rows = await used.ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<bool> ClaimAsync(Subscriber subscriber, string key, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7(now);
        var claimed = subscriber.UserId is { } userId
            ? await context.Database.ExecuteSqlAsync($"""
                INSERT INTO kimlik.usage_records (id, user_id, key, created_at) VALUES ({id}, {userId}, {key}, {now}) ON CONFLICT DO NOTHING
                """, cancellationToken)
            : await context.Database.ExecuteSqlAsync($"""
                INSERT INTO kimlik.usage_records (id, organization_id, key, created_at) VALUES ({id}, {subscriber.OrganizationId}, {key}, {now}) ON CONFLICT DO NOTHING
                """, cancellationToken);
        return claimed == 1;
    }
}
