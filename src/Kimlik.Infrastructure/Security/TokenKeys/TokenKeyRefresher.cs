using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>
/// Refreshes the key ring and, when the keys changed, drops the cached OpenID Connect server options so the
/// next request rebuilds them with the new credentials.
/// </summary>
internal sealed class TokenKeyRefresher(TokenKeyRing keyRing, IOptionsMonitorCache<OpenIddictServerOptions> serverOptionsCache)
{
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await keyRing.RefreshAsync(cancellationToken))
        {
            return false;
        }

        serverOptionsCache.TryRemove(Options.DefaultName);
        return true;
    }
}
