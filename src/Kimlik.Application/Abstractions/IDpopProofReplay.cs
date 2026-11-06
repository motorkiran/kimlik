namespace Kimlik.Application.Abstractions;

/// <summary>Remembers the DPoP proofs Kimlik accepted, across instances, so that none is accepted twice (RFC 9449, section 11.1).</summary>
public interface IDpopProofReplay
{
    /// <summary>
    /// Records the proof, by a hash that tells it apart from every other one, until it is too old to be accepted anyway;
    /// <see langword="false"/> if it was recorded before.
    /// </summary>
    Task<bool> TryUseAsync(string hash, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}
