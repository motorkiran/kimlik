using System.Net;
using System.Net.Http.Headers;

namespace Kimlik.Client.Tokens;

/// <summary>
/// Adds the service client's access token to requests. When Kimlik rejects a token, for example after it was
/// revoked, the handler gets a new one and tries once more.
/// </summary>
internal sealed class AccessTokenHandler(ClientCredentialsTokenSource tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(renew: false, cancellationToken));
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(renew: true, cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }
}
