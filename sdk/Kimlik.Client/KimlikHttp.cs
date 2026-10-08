using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Kimlik.Client;

/// <summary>Sends Management API requests and turns problem details into <see cref="KimlikApiException"/>.</summary>
internal sealed class KimlikHttp(HttpClient http)
{
    private const string BasePath = "api/v1/";

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(BasePath + path, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await http.SendAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    public async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>A path with query parameters; parameters without a value are left out.</summary>
    public static string WithQuery(string path, params (string Name, object? Value)[] parameters)
    {
        var query = new StringBuilder();
        foreach (var (name, value) in parameters)
        {
            var text = value switch
            {
                null => null,
                Enum enumValue => JsonNamingPolicy.CamelCase.ConvertName(enumValue.ToString()),
                DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };

            if (!string.IsNullOrEmpty(text))
            {
                query.Append(query.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(text));
            }
        }

        return path + query;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body) => new(method, BasePath + path)
    {
        Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: KimlikJson.Options),
    };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(KimlikJson.Options, cancellationToken)
            ?? throw new KimlikApiException(response.StatusCode, code: null, "Kimlik returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        Problem? problem = null;
        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            problem = await response.Content.ReadFromJsonAsync<Problem>(KimlikJson.Options, cancellationToken);
        }

        throw new KimlikApiException(
            response.StatusCode,
            problem?.Code,
            problem?.Detail ?? problem?.Title ?? $"Kimlik answered {(int)response.StatusCode} {response.ReasonPhrase}.",
            problem?.Errors);
    }

    private sealed record Problem(string? Title, string? Detail, string? Code, Dictionary<string, string[]>? Errors);
}
