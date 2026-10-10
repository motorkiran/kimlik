using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>
/// Use of metered limits, counted by calendar month in UTC. Requires <c>kimlik.usage:write</c> to record use, and
/// <c>kimlik.usage:read</c> to read it.
/// </summary>
public sealed class UsageClient
{
    private readonly KimlikHttp _http;

    internal UsageClient(KimlikHttp http) => _http = http;

    /// <summary>
    /// Records use. With <see cref="RecordUsageRequest.Enforce"/>, use past the month's limit is refused with a
    /// <see cref="KimlikApiException"/> whose code is <c>usage.limit_reached</c>, and not recorded.
    /// </summary>
    public Task<UsageResponse> RecordAsync(RecordUsageRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<UsageResponse>(HttpMethod.Post, "usage", request, cancellationToken);

    /// <summary>A user's or an organization's use of every metered limit this month.</summary>
    public Task<IReadOnlyList<UsageResponse>> GetAsync(SubscriberType subscriberType, Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<IReadOnlyList<UsageResponse>>($"usage/{(subscriberType == SubscriberType.User ? "user" : "organization")}/{id}", cancellationToken);
}
