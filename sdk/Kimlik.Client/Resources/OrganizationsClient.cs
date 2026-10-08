using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>
/// Organizations and their members. Requires <c>kimlik.organizations:read</c>, or <c>kimlik.organizations:write</c>
/// to change them.
/// </summary>
public sealed class OrganizationsClient
{
    private readonly KimlikHttp _http;

    internal OrganizationsClient(KimlikHttp http) => _http = http;

    public Task<Page<OrganizationResponse>> ListAsync(string? search = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<OrganizationResponse>>(KimlikHttp.WithQuery("organizations", ("q", search), ("cursor", cursor), ("limit", limit)), cancellationToken);

    public Task<OrganizationResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<OrganizationResponse>($"organizations/{id}", cancellationToken);

    public Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<OrganizationResponse>(HttpMethod.Post, "organizations", request, cancellationToken);

    public Task<OrganizationResponse> UpdateAsync(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<OrganizationResponse>(HttpMethod.Patch, $"organizations/{id}", request, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"organizations/{id}", body: null, cancellationToken);

    public Task<Page<MemberResponse>> ListMembersAsync(Guid id, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<MemberResponse>>(KimlikHttp.WithQuery($"organizations/{id}/members", ("cursor", cursor), ("limit", limit)), cancellationToken);

    public Task<MemberResponse> AddMemberAsync(Guid id, AddMemberRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<MemberResponse>(HttpMethod.Post, $"organizations/{id}/members", request, cancellationToken);

    /// <summary>Replaces a member's organization roles.</summary>
    public Task<MemberResponse> SetMemberRolesAsync(Guid id, Guid userId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default) =>
        _http.SendAsync<MemberResponse>(HttpMethod.Put, $"organizations/{id}/members/{userId}/roles", new SetRolesRequest { Roles = roles }, cancellationToken);

    public Task RemoveMemberAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"organizations/{id}/members/{userId}", body: null, cancellationToken);
}
