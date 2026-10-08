using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>Features and plans. Requires <c>kimlik.plans:read</c>, or <c>kimlik.plans:write</c> to change them.</summary>
public sealed class PlansClient
{
    private readonly KimlikHttp _http;

    internal PlansClient(KimlikHttp http) => _http = http;

    public Task<Page<FeatureResponse>> ListFeaturesAsync(string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<FeatureResponse>>(KimlikHttp.WithQuery("features", ("cursor", cursor), ("limit", limit)), cancellationToken);

    public Task<FeatureResponse> CreateFeatureAsync(CreateFeatureRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<FeatureResponse>(HttpMethod.Post, "features", request, cancellationToken);

    public Task<FeatureResponse> UpdateFeatureAsync(Guid id, UpdateFeatureRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<FeatureResponse>(HttpMethod.Patch, $"features/{id}", request, cancellationToken);

    public Task DeleteFeatureAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"features/{id}", body: null, cancellationToken);

    /// <param name="archived">Only archived plans, or only plans open to new subscribers; all plans when omitted.</param>
    /// <param name="cursor">The <see cref="Page{T}.NextCursor"/> of the previous page.</param>
    /// <param name="limit">The page size, 50 by default and at most 200.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<Page<PlanResponse>> ListAsync(bool? archived = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<PlanResponse>>(KimlikHttp.WithQuery("plans", ("archived", archived), ("cursor", cursor), ("limit", limit)), cancellationToken);

    public Task<PlanResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<PlanResponse>($"plans/{id}", cancellationToken);

    public Task<PlanResponse> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<PlanResponse>(HttpMethod.Post, "plans", request, cancellationToken);

    public Task<PlanResponse> UpdateAsync(Guid id, UpdatePlanRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<PlanResponse>(HttpMethod.Patch, $"plans/{id}", request, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"plans/{id}", body: null, cancellationToken);
}
