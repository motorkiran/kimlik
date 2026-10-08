using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>The APIs that accept Kimlik tokens. Requires <c>kimlik.clients:read</c>, or <c>kimlik.clients:write</c> to change them.</summary>
public sealed class ApiResourcesClient
{
    private readonly KimlikHttp _http;

    internal ApiResourcesClient(KimlikHttp http) => _http = http;

    public Task<Page<ApiResourceResponse>> ListAsync(string? search = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<ApiResourceResponse>>(KimlikHttp.WithQuery("api-resources", ("q", search), ("cursor", cursor), ("limit", limit)), cancellationToken);

    public Task<ApiResourceResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<ApiResourceResponse>($"api-resources/{id}", cancellationToken);

    public Task<ApiResourceResponse> CreateAsync(CreateApiResourceRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<ApiResourceResponse>(HttpMethod.Post, "api-resources", request, cancellationToken);

    public Task<ApiResourceResponse> UpdateAsync(Guid id, UpdateApiResourceRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<ApiResourceResponse>(HttpMethod.Patch, $"api-resources/{id}", request, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"api-resources/{id}", body: null, cancellationToken);
}
