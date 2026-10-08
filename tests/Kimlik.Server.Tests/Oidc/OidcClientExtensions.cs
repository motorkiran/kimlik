using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>Raw protocol calls, so the tests exercise the wire format rather than a client library.</summary>
internal static class OidcClientExtensions
{
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string path, IEnumerable<KeyValuePair<string, string>> fields, TestClient? authenticatedAs = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) };

        if (authenticatedAs is not null)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(authenticatedAs.ClientId)}:{Uri.EscapeDataString(authenticatedAs.ClientSecret)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<string> RequestClientCredentialsTokenAsync(this HttpClient client, TestClient serviceClient)
    {
        using var response = await client.PostFormAsync(
            "/connect/token",
            [new("grant_type", "client_credentials"), new("scope", TestClients.ApiScope)],
            serviceClient);

        var body = await response.ReadJsonAsync();
        response.EnsureSuccessStatusCode();
        return body.GetProperty("access_token").GetString()!;
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
}
