using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>
/// Subscriptions and entitlements, which a billing integration keeps up to date. Requires <c>kimlik.subscriptions:read</c>,
/// or <c>kimlik.subscriptions:write</c> to change them.
/// </summary>
public sealed class SubscriptionsClient
{
    private readonly KimlikHttp _http;

    internal SubscriptionsClient(KimlikHttp http) => _http = http;

    /// <summary>Lists subscriptions newest first, ended ones included; pass a subscriber to see its history.</summary>
    public Task<Page<SubscriptionResponse>> ListAsync(
        SubscriberType? subscriberType = null, Guid? subscriberId = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<SubscriptionResponse>>(
            KimlikHttp.WithQuery("subscriptions", ("subscriberType", subscriberType), ("subscriberId", subscriberId), ("cursor", cursor), ("limit", limit)),
            cancellationToken);

    public Task<SubscriptionResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<SubscriptionResponse>($"subscriptions/{id}", cancellationToken);

    public Task<SubscriptionResponse> CreateAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions", request, cancellationToken);

    /// <summary>Changes what <paramref name="request"/> sets, such as the plan or the period end after a renewal.</summary>
    public Task<SubscriptionResponse> UpdateAsync(Guid id, UpdateSubscriptionRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<SubscriptionResponse>(HttpMethod.Patch, $"subscriptions/{id}", request, cancellationToken);

    /// <summary>Cancels at the end of the period; a subscription without an end expires at once.</summary>
    public Task<SubscriptionResponse> CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync<SubscriptionResponse>(HttpMethod.Post, $"subscriptions/{id}/cancel", body: null, cancellationToken);

    /// <summary>The plan in effect for a user or an organization, and the value of every feature.</summary>
    public Task<EntitlementsResponse> GetEntitlementsAsync(SubscriberType subscriberType, Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<EntitlementsResponse>(
            $"entitlements/{(subscriberType == SubscriberType.User ? "user" : "organization")}/{id}", cancellationToken);
}
