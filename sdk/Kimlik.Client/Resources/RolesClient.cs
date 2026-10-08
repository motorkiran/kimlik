using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>Roles and their permissions. Requires <c>kimlik.roles:read</c>, or <c>kimlik.roles:write</c> to change them.</summary>
public sealed class RolesClient
{
    private readonly KimlikHttp _http;

    internal RolesClient(KimlikHttp http) => _http = http;

    public Task<Page<RoleResponse>> ListAsync(
        string? search = null, RoleScope? scope = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<RoleResponse>>(KimlikHttp.WithQuery("roles", ("q", search), ("scope", scope), ("cursor", cursor), ("limit", limit)), cancellationToken);

    public Task<RoleResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<RoleResponse>($"roles/{id}", cancellationToken);

    public Task<RoleResponse> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<RoleResponse>(HttpMethod.Post, "roles", request, cancellationToken);

    public Task<RoleResponse> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<RoleResponse>(HttpMethod.Patch, $"roles/{id}", request, cancellationToken);

    /// <summary>Replaces the role's permissions; tokens issued from then on carry the new ones.</summary>
    public Task<RoleResponse> SetPermissionsAsync(Guid id, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default) =>
        _http.SendAsync<RoleResponse>(HttpMethod.Put, $"roles/{id}/permissions", new SetPermissionsRequest { Permissions = permissions }, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"roles/{id}", body: null, cancellationToken);
}
