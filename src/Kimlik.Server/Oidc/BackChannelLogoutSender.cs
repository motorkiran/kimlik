using Kimlik.Application.Accounts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Kimlik.Server.Oidc;

/// <summary>
/// Posts logout tokens (OpenID Connect Back-Channel Logout 1.0): JWTs signed with the active token signing key, typed
/// <c>logout+jwt</c>, that tell a web client that a user's session, or all of their sessions, ended. Unlike webhooks,
/// they may go to private networks, where apps often run beside Kimlik; only administrators who manage clients set the
/// endpoints.
/// </summary>
internal sealed class BackChannelLogoutSender(IHttpClientFactory httpClients, IOptionsMonitor<OpenIddictServerOptions> server, TimeProvider timeProvider)
    : IBackChannelLogoutSender
{
    public const string HttpClientName = "Kimlik.BackChannelLogout";

    private const string LogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public async Task SendAsync(Uri endpoint, string clientId, Guid userId, string? sessionId, CancellationToken cancellationToken)
    {
        var options = server.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = userId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            ["events"] = new Dictionary<string, object> { [LogoutEvent] = new Dictionary<string, object>() },
        };

        if (sessionId is not null)
        {
            claims[JwtRegisteredClaimNames.Sid] = sessionId;
        }

        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer!.AbsoluteUri,
            Audience = clientId,
            IssuedAt = now.UtcDateTime,
            Expires = (now + Lifetime).UtcDateTime,
            Claims = claims,
            TokenType = "logout+jwt",
            SigningCredentials = options.SigningCredentials.First(credentials => credentials.Key is AsymmetricSecurityKey),
        });

        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("logout_token", token)]);
        using var response = await httpClients.CreateClient(HttpClientName).PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The outbox tries again later.
            throw new HttpRequestException($"The back-channel logout endpoint of {clientId} answered {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }
}
