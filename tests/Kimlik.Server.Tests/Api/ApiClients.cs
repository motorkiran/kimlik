using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kimlik.Contracts;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Oidc;

namespace Kimlik.Server.Tests.Api;

/// <summary>An HTTP client that calls the Management API as a service client.</summary>
internal sealed record ApiClient(HttpClient Http, string ClientId) : IDisposable
{
    public void Dispose() => Http.Dispose();
}

internal static class ApiClients
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    /// <summary>A new service client for Kimlik's API holding <paramref name="role"/>, or no role at all.</summary>
    public static async Task<ApiClient> CreateApiClientAsync(this KimlikServerFixture server, string? role = SystemRoles.Admin)
    {
        var serviceClient = await server.CreateServiceClientAsync(KimlikScopes.Api);
        if (role is not null)
        {
            await server.AssignToClientAsync(serviceClient.ClientId, role);
        }

        var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await http.RequestClientCredentialsTokenAsync(serviceClient, KimlikScopes.Api));
        return new ApiClient(http, serviceClient.ClientId);
    }

    /// <summary>An access token for Kimlik's API, issued to <paramref name="user"/> through a web client.</summary>
    public static async Task<string> UserAccessTokenAsync(this KimlikServerFixture server, TestUser user)
    {
        var (browser, _, tokens) = await server.SignInAndRedeemAsync(user, $"openid {KimlikScopes.Api}");
        browser.Dispose();
        return tokens.GetProperty("access_token").GetString()!;
    }

    public static HttpClient WithToken(this KimlikServerFixture server, string accessToken)
    {
        var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return http;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.Current.CancellationToken))!;

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient http, string path, T body) =>
        http.PostAsJsonAsync(path, body, Json, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PostAsync(this HttpClient http, string path) =>
        http.PostAsync(path, content: null, TestContext.Current.CancellationToken);

    /// <summary>Sends a raw JSON body, for requests whose exact shape matters.</summary>
    public static async Task<HttpResponseMessage> SendJsonAsync(this HttpClient http, HttpMethod method, string path, string json)
    {
        using var request = new HttpRequestMessage(method, path) { Content = new StringContent(json, MediaTypeHeaderValue.Parse("application/json")) };
        return await http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient http, string path, T body) =>
        http.PutAsJsonAsync(path, body, Json, TestContext.Current.CancellationToken);

    /// <summary>The <c>code</c> of a problem details response.</summary>
    public static async Task<string?> ReadProblemCodeAsync(this HttpResponseMessage response) =>
        (await response.ReadJsonAsync()).TryGetProperty("code", out var code) ? code.GetString() : null;
}
