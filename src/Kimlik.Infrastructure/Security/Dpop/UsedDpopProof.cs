using Kimlik.Application.Abstractions;
using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Infrastructure.Security.Dpop;

/// <summary>A DPoP proof Kimlik accepted, kept until it is too old to be accepted anyway.</summary>
public sealed class UsedDpopProof
{
    public const int HashLength = 64;

    /// <summary>The hex SHA-256 hash of the proof's key thumbprint and <c>jti</c>.</summary>
    public required string Hash { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

internal sealed class DpopProofReplay(KimlikDbContext context) : IDpopProofReplay
{
    public async Task<bool> TryUseAsync(string hash, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        // One statement, so two instances given the same proof at once cannot both accept it.
        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO kimlik.used_dpop_proofs (hash, expires_at) VALUES ({hash}, {expiresAt.ToUniversalTime()}) ON CONFLICT (hash) DO NOTHING",
            cancellationToken) == 1;
}
