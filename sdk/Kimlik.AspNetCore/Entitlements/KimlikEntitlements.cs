using System.Text.Json;
using Kimlik.Client;
using Kimlik.Contracts.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kimlik.AspNetCore.Entitlements;

/// <summary>How long plan definitions are cached before they are fetched again.</summary>
public sealed class KimlikEntitlementsOptions
{
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Reads feature values from plan definitions, fetched through <see cref="KimlikClient"/> and cached. The client is
/// resolved for each fetch, so its HTTP handlers keep rotating as <see cref="IHttpClientFactory"/> intends.
/// </summary>
internal sealed class KimlikEntitlements(IServiceProvider services, IOptions<KimlikEntitlementsOptions> options, TimeProvider timeProvider)
    : IKimlikEntitlements, IDisposable
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private (IReadOnlyDictionary<string, PlanResponse> Plans, DateTimeOffset FetchedAt)? _cache;

    public async Task<bool> HasFeatureAsync(KimlikUser caller, string feature, CancellationToken cancellationToken = default) =>
        await ValueAsync(caller, feature, cancellationToken) is { ValueKind: JsonValueKind.True };

    public async Task<long?> GetLimitAsync(KimlikUser caller, string feature, CancellationToken cancellationToken = default) =>
        await ValueAsync(caller, feature, cancellationToken) switch
        {
            { ValueKind: JsonValueKind.Null } => null,
            { ValueKind: JsonValueKind.Number } value when value.TryGetInt64(out var limit) => limit,
            _ => 0,
        };

    public void Dispose() => _refreshLock.Dispose();

    private async Task<JsonElement?> ValueAsync(KimlikUser caller, string feature, CancellationToken cancellationToken)
    {
        if (caller.Plan is not { } planKey)
        {
            return null;
        }

        var plans = await PlansAsync(cancellationToken);
        return plans.TryGetValue(planKey, out var plan) && plan.Features.TryGetValue(feature, out var value) ? value : null;
    }

    private async Task<IReadOnlyDictionary<string, PlanResponse>> PlansAsync(CancellationToken cancellationToken)
    {
        if (_cache is { } cached && timeProvider.GetUtcNow() - cached.FetchedAt < options.Value.CacheDuration)
        {
            return cached.Plans;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is { } fresh && timeProvider.GetUtcNow() - fresh.FetchedAt < options.Value.CacheDuration)
            {
                return fresh.Plans;
            }

            // Archived plans included: their subscribers keep them.
            var kimlik = services.GetRequiredService<KimlikClient>();
            var plans = new Dictionary<string, PlanResponse>(StringComparer.Ordinal);
            string? cursor = null;
            do
            {
                var page = await kimlik.Plans.ListAsync(cursor: cursor, limit: 200, cancellationToken: cancellationToken);
                foreach (var plan in page.Items)
                {
                    plans[plan.Key] = plan;
                }

                cursor = page.NextCursor;
            }
            while (cursor is not null);

            _cache = (plans, timeProvider.GetUtcNow());
            return plans;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
