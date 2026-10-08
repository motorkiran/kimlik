using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>The applications that request tokens. Requires <c>kimlik.clients:read</c>, or <c>kimlik.clients:write</c> to change them.</summary>
public sealed class ClientsClient
{
    private readonly KimlikHttp _http;

    internal ClientsClient(KimlikHttp http) => _http = http;

    public Task<Page<ClientResponse>> ListAsync(string? search = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<ClientResponse>>(KimlikHttp.WithQuery("clients", ("q", search), ("cursor", cursor), ("limit", limit)), cancellationToken);

    public Task<ClientResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<ClientResponse>($"clients/{id}", cancellationToken);

    /// <summary>Registers a client; the response carries the secret of a web or service client, which is not shown again.</summary>
    public Task<CreatedClientResponse> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<CreatedClientResponse>(HttpMethod.Post, "clients", request, cancellationToken);

    public Task<ClientResponse> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<ClientResponse>(HttpMethod.Patch, $"clients/{id}", request, cancellationToken);

    /// <summary>Replaces the client's secret; the previous one stops working at once.</summary>
    public Task<ClientSecretResponse> RegenerateSecretAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync<ClientSecretResponse>(HttpMethod.Post, $"clients/{id}/secret", body: null, cancellationToken);

    /// <summary>Replaces the global roles of a service client.</summary>
    public Task<ClientResponse> SetRolesAsync(Guid id, IReadOnlyList<string> roles, CancellationToken cancellationToken = default) =>
        _http.SendAsync<ClientResponse>(HttpMethod.Put, $"clients/{id}/roles", new SetRolesRequest { Roles = roles }, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"clients/{id}", body: null, cancellationToken);
}
