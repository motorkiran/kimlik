using System.Net.Http.Headers;

namespace Kimlik.Server.SocialLogin;

/// <summary>
/// The address of a GitHub account. The profile leaves it out when its owner keeps it private, but GitHub lists the
/// account's addresses, and whether each is verified, to apps granted the <c>user:email</c> scope.
/// </summary>
public sealed class GitHubEmails(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "kimlik.github";

    /// <summary>The primary address if it is verified, else another verified one.</summary>
    public async Task<string?> FindVerifiedAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, "user/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var emails = await response.Content.ReadFromJsonAsync<GitHubEmail[]>(cancellationToken) ?? [];
        return emails.Where(email => email.Verified).OrderByDescending(email => email.Primary).Select(email => email.Email).FirstOrDefault();
    }

    private sealed record GitHubEmail(string Email, bool Primary, bool Verified);
}
