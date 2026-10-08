namespace Kimlik.Server.Oidc;

/// <summary>Token lifetimes from the <c>Kimlik:Tokens</c> configuration section.</summary>
public sealed class TokenOptions
{
    public const string SectionName = "Kimlik:Tokens";

    /// <summary>Kept short so that role, permission and plan changes reach resource servers quickly.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan IdentityTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Sliding lifetime: every refresh issues a new refresh token valid for this long.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// How long a refresh token that was already used is still accepted, so that concurrent refreshes from one
    /// client do not log it out. Any later reuse is treated as theft and revokes the whole token family.
    /// </summary>
    public TimeSpan RefreshTokenReuseLeeway { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Upper bound for a chain of refreshes, after which the user must sign in again.</summary>
    public TimeSpan RefreshTokenAbsoluteLifetime { get; set; } = TimeSpan.FromDays(90);

    internal bool IsValid() =>
        AccessTokenLifetime > TimeSpan.Zero
        && IdentityTokenLifetime > TimeSpan.Zero
        && AuthorizationCodeLifetime > TimeSpan.Zero
        && RefreshTokenLifetime > TimeSpan.Zero
        && RefreshTokenReuseLeeway >= TimeSpan.Zero
        && RefreshTokenAbsoluteLifetime >= RefreshTokenLifetime;
}
