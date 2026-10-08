using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>
/// The API keys of users and organizations, which their owners create through the Account API. Requires
/// <c>kimlik.api_keys:read</c> to list them, <c>kimlik.api_keys:write</c> to revoke them and
/// <c>kimlik.api_keys:verify</c> to verify them.
/// </summary>
public sealed class ApiKeysClient
{
    private readonly KimlikHttp _http;

    internal ApiKeysClient(KimlikHttp http) => _http = http;

    /// <summary>Lists keys in creation order, revoked and expired ones included; pass an owner to see only its keys.</summary>
    public Task<Page<ApiKeyResponse>> ListAsync(
        Guid? userId = null, Guid? organizationId = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<ApiKeyResponse>>(
            KimlikHttp.WithQuery("api-keys", ("userId", userId), ("organizationId", organizationId), ("cursor", cursor), ("limit", limit)),
            cancellationToken);

    public Task RevokeAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"api-keys/{id}", body: null, cancellationToken);

    /// <summary>Whether a key that a caller sent works, whose it is and what it may do now.</summary>
    public Task<ApiKeyVerificationResponse> VerifyAsync(string key, CancellationToken cancellationToken = default) =>
        _http.SendAsync<ApiKeyVerificationResponse>(HttpMethod.Post, "api-keys/verify", new VerifyApiKeyRequest { Key = key }, cancellationToken);
}
