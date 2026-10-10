using Kimlik.Domain.Plans;

namespace Kimlik.Application.Plans;

/// <summary>Counts metered use atomically, within the caller's transaction, which the database does best.</summary>
public interface IUsageCounters
{
    /// <summary>
    /// Adds <paramref name="quantity"/> to the subscriber's counter for the feature and month and returns the new use;
    /// with <paramref name="limit"/>, only if the sum stays within it, returning <see langword="null"/> when it would not.
    /// </summary>
    Task<long?> AddAsync(Subscriber subscriber, Guid featureId, DateOnly period, long quantity, long? limit, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Takes an idempotency key for the subscriber; <see langword="false"/> when a request with it came already.</summary>
    Task<bool> ClaimAsync(Subscriber subscriber, string key, DateTimeOffset now, CancellationToken cancellationToken);
}
