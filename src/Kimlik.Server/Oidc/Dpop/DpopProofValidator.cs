using Kimlik.Application.Abstractions;
using Kimlik.Security;
using Kimlik.Server.Hosting;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Oidc.Dpop;

/// <summary>Checks the DPoP proofs of requests to Kimlik, and that each is used once, across instances.</summary>
internal sealed class DpopProofValidator(IDpopProofReplay replay, IOptions<ServerOptions> server, TimeProvider timeProvider)
{
    /// <summary>
    /// The request's proof, made for <paramref name="accessToken"/> if one is given: <see langword="null"/> with an error
    /// if it is invalid or was used before, and without one if there is none.
    /// </summary>
    public async Task<(DpopProof? Proof, string? Error)> ValidateAsync(HttpRequest request, string? accessToken, CancellationToken cancellationToken)
    {
        // Clients make proofs for the URLs that discovery gives them, which start with the public URL whatever the proxy sends on.
        var target = new Uri(server.Value.PublicUrl!.AbsoluteUri.TrimEnd('/') + request.Path.ToUriComponent());
        var proof = DpopProof.Read(request.Headers[DpopProof.HeaderName], request.Method, target, accessToken, timeProvider.GetUtcNow(), out var error);
        if (proof is null)
        {
            return (null, error);
        }

        return await replay.TryUseAsync(proof.ReplayKey, proof.UsableUntil, cancellationToken)
            ? (proof, null)
            : (null, "The DPoP proof was used before.");
    }
}
