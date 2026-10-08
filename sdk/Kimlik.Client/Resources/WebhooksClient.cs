using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>
/// Webhook endpoints and the log of their deliveries. Requires <c>kimlik.webhooks:read</c>, or
/// <c>kimlik.webhooks:write</c> to change them.
/// </summary>
public sealed class WebhooksClient
{
    private readonly KimlikHttp _http;

    internal WebhooksClient(KimlikHttp http) => _http = http;

    public Task<IReadOnlyList<string>> ListEventTypesAsync(CancellationToken cancellationToken = default) =>
        _http.GetAsync<IReadOnlyList<string>>("webhooks/event-types", cancellationToken);

    public Task<Page<WebhookEndpointResponse>> ListEndpointsAsync(string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<WebhookEndpointResponse>>(KimlikHttp.WithQuery("webhooks/endpoints", ("cursor", cursor), ("limit", limit)), cancellationToken);

    /// <summary>Registers an endpoint; the response holds its signing secret, which is not shown again.</summary>
    public Task<CreatedWebhookEndpointResponse> CreateEndpointAsync(CreateWebhookEndpointRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<CreatedWebhookEndpointResponse>(HttpMethod.Post, "webhooks/endpoints", request, cancellationToken);

    public Task<WebhookEndpointResponse> GetEndpointAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<WebhookEndpointResponse>($"webhooks/endpoints/{id}", cancellationToken);

    /// <summary>Changes the properties <paramref name="request"/> sets; the others keep their value.</summary>
    public Task<WebhookEndpointResponse> UpdateEndpointAsync(Guid id, UpdateWebhookEndpointRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<WebhookEndpointResponse>(HttpMethod.Patch, $"webhooks/endpoints/{id}", request, cancellationToken);

    public Task DeleteEndpointAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"webhooks/endpoints/{id}", body: null, cancellationToken);

    /// <summary>Replaces the endpoint's signing secret and returns the new one.</summary>
    public Task<WebhookSecretResponse> RotateSecretAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync<WebhookSecretResponse>(HttpMethod.Post, $"webhooks/endpoints/{id}/secret", body: null, cancellationToken);

    /// <summary>Sends a <c>webhook.test</c> event to the endpoint.</summary>
    public Task<WebhookDeliveryResponse> SendTestAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync<WebhookDeliveryResponse>(HttpMethod.Post, $"webhooks/endpoints/{id}/test", body: null, cancellationToken);

    /// <summary>Lists deliveries newest first.</summary>
    public Task<Page<WebhookDeliveryResponse>> ListDeliveriesAsync(
        Guid? endpointId = null, WebhookDeliveryStatus? status = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<WebhookDeliveryResponse>>(
            KimlikHttp.WithQuery("webhooks/deliveries", ("endpointId", endpointId), ("status", status), ("cursor", cursor), ("limit", limit)),
            cancellationToken);

    public Task<WebhookDeliveryResponse> GetDeliveryAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<WebhookDeliveryResponse>($"webhooks/deliveries/{id}", cancellationToken);

    /// <summary>Sends the delivery's event again, with the same <c>webhook-id</c>.</summary>
    public Task<WebhookDeliveryResponse> RedeliverAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync<WebhookDeliveryResponse>(HttpMethod.Post, $"webhooks/deliveries/{id}/redeliver", body: null, cancellationToken);
}
