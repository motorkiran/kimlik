using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>An authorization request as a client would build it, with fresh PKCE, state and nonce values.</summary>
internal sealed class AuthorizationRequest(string clientId)
{
    public string CodeVerifier { get; } = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public string State { get; } = Guid.NewGuid().ToString("N");

    public string Nonce { get; } = Guid.NewGuid().ToString("N");

    public string Scope { get; init; } = $"openid profile email offline_access {TestClients.ApiScope}";

    public string? Prompt { get; init; }

    /// <summary>The organization to sign in to, by ID or slug.</summary>
    public string? Organization { get; init; }

    public string RedirectUri { get; init; } = TestWebClient.RedirectUri;

    public string Url => QueryHelpers.AddQueryString("/connect/authorize", Parameters);

    /// <summary>The request's parameters, as sent in the URL or pushed to the server (RFC 9126).</summary>
    public Dictionary<string, string?> Parameters
    {
        get
        {
            var parameters = new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["scope"] = Scope,
                ["state"] = State,
                ["nonce"] = Nonce,
                ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(CodeVerifier))),
                ["code_challenge_method"] = "S256",
            };

            if (Organization is not null)
            {
                parameters["organization"] = Organization;
            }

            if (Prompt is not null)
            {
                parameters["prompt"] = Prompt;
            }

            return parameters;
        }
    }

    /// <summary>Reads the parameters the server sent back to the client's redirect URI.</summary>
    public static Dictionary<string, string> ReadCallback(HttpResponseMessage response)
    {
        var location = response.Headers.Location ?? throw new InvalidOperationException($"Expected a redirect, got {(int)response.StatusCode}.");
        location.GetLeftPart(UriPartial.Path).ShouldBe(TestWebClient.RedirectUri);

        return QueryHelpers.ParseQuery(location.Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
    }
}
