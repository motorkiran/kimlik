using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>The permission catalog. Requires <c>kimlik.roles:read</c>, or <c>kimlik.roles:write</c> to change it.</summary>
public sealed class PermissionsClient
{
    private readonly KimlikHttp _http;

    internal PermissionsClient(KimlikHttp http) => _http = http;

    public Task<Page<PermissionResponse>> ListAsync(string? search = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<PermissionResponse>>(KimlikHttp.WithQuery("permissions", ("q", search), ("cursor", cursor), ("limit", limit)), cancellationToken);

    public Task<PermissionResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<PermissionResponse>($"permissions/{id}", cancellationToken);

    public Task<PermissionResponse> CreateAsync(CreatePermissionRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<PermissionResponse>(HttpMethod.Post, "permissions", request, cancellationToken);

    public Task<PermissionResponse> UpdateAsync(Guid id, UpdatePermissionRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<PermissionResponse>(HttpMethod.Patch, $"permissions/{id}", request, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"permissions/{id}", body: null, cancellationToken);
}
