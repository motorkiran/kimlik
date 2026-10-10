using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>
/// Organizations' own identity providers, which sign in the people whose addresses are in their domains. Requires
/// <c>kimlik.organizations:read</c>, or <c>kimlik.organizations:write</c> and every installation-wide system permission
/// to change them.
/// </summary>
public sealed class SsoConnectionsClient
{
    private readonly KimlikHttp _http;

    internal SsoConnectionsClient(KimlikHttp http) => _http = http;

    public Task<Page<SsoConnectionResponse>> ListAsync(
        Guid? organizationId = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<SsoConnectionResponse>>(
            KimlikHttp.WithQuery("sso-connections", ("organizationId", organizationId), ("cursor", cursor), ("limit", limit)), cancellationToken);

    public Task<SsoConnectionResponse> CreateAsync(CreateSsoConnectionRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<SsoConnectionResponse>(HttpMethod.Post, "sso-connections", request, cancellationToken);

    public Task<SsoConnectionResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<SsoConnectionResponse>($"sso-connections/{id}", cancellationToken);

    /// <summary>Changes the properties <paramref name="request"/> sets; the others keep their value.</summary>
    public Task<SsoConnectionResponse> UpdateAsync(Guid id, UpdateSsoConnectionRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<SsoConnectionResponse>(HttpMethod.Patch, $"sso-connections/{id}", request, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"sso-connections/{id}", body: null, cancellationToken);
}
