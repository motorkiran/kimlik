using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Kimlik.Client.Tokens;

/// <summary>
/// Gets access tokens with the client credentials grant and reuses each one until shortly before it expires. The
/// token endpoint comes from Kimlik's discovery document.
/// </summary>
internal sealed class ClientCredentialsTokenSource(IHttpClientFactory httpClientFactory, IOptions<KimlikClientOptions> options, TimeProvider timeProvider) : IDisposable
{
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Uri? _tokenEndpoint;
    private (string Value, DateTimeOffset RenewAt)? _token;

    public async ValueTask<string> GetTokenAsync(bool renew, CancellationToken cancellationToken)
    {
        if (!renew && _token is { } cached && timeProvider.GetUtcNow() < cached.RenewAt)
        {
            return cached.Value;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have renewed the token while this one waited.
            if (_token is { } current && timeProvider.GetUtcNow() < current.RenewAt && !renew)
            {
                return current.Value;
            }

            var token = await RequestTokenAsync(cancellationToken);
            _token = token;
            return token.Value;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task<(string Value, DateTimeOffset RenewAt)> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var http = httpClientFactory.CreateClient(KimlikClientDefaults.TokenHttpClientName);

        _tokenEndpoint ??= await DiscoverTokenEndpointAsync(http, settings.BaseAddress, cancellationToken);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = settings.ClientId!,
            ["client_secret"] = settings.ClientSecret!,
            ["scope"] = settings.Scope,
        });

        var requestedAt = timeProvider.GetUtcNow();
        using var response = await http.PostAsync(_tokenEndpoint, content, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);

        if (!response.IsSuccessStatusCode || body?.AccessToken is not { Length: > 0 } accessToken)
        {
            throw new KimlikApiException(
                response.StatusCode,
                body?.Error,
                $"Kimlik refused the client credentials of '{settings.ClientId}': {body?.ErrorDescription ?? body?.Error ?? response.ReasonPhrase}");
        }

        var lifetime = TimeSpan.FromSeconds(body.ExpiresIn ?? 0);
        return (accessToken, requestedAt + (lifetime > 2 * RenewalMargin ? lifetime - RenewalMargin : lifetime / 2));
    }

    private static async Task<Uri> DiscoverTokenEndpointAsync(HttpClient http, Uri authority, CancellationToken cancellationToken)
    {
        var discovery = await http.GetFromJsonAsync<DiscoveryDocument>(new Uri(authority, ".well-known/openid-configuration"), cancellationToken);
        return discovery?.TokenEndpoint
            ?? throw new InvalidOperationException($"The discovery document of {authority} has no token endpoint.");
    }

    private sealed record DiscoveryDocument([property: JsonPropertyName("token_endpoint")] Uri? TokenEndpoint);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
