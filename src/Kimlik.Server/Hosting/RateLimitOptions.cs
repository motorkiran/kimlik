using System.ComponentModel.DataAnnotations;

namespace Kimlik.Server.Hosting;

/// <summary>
/// Requests per minute allowed from one network address, from the <c>Kimlik:RateLimits</c> configuration
/// section. IPv6 addresses are grouped by /64, the block a single subscriber usually controls.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "Kimlik:RateLimits";

    /// <summary>Sign-in attempts. Account lockout additionally limits attempts per account.</summary>
    [Range(1, 100_000)]
    public int SignInsPerMinute { get; set; } = 10;

    [Range(1, 100_000)]
    public int SignUpsPerMinute { get; set; } = 10;

    /// <summary>Requests that send email, such as password resets and verification links.</summary>
    [Range(1, 100_000)]
    public int EmailRequestsPerMinute { get; set; } = 5;

    /// <summary>Token, introspection and revocation requests.</summary>
    [Range(1, 1_000_000)]
    public int ProtocolRequestsPerMinute { get; set; } = 300;
}
