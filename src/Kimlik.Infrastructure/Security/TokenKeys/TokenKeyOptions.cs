namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>Rotation schedule for token keys, from the <c>Kimlik:Tokens:Keys</c> configuration section.</summary>
public sealed class TokenKeyOptions
{
    public const string SectionName = "Kimlik:Tokens:Keys";

    /// <summary>How long a key signs or encrypts new tokens before its successor takes over.</summary>
    public TimeSpan RotationInterval { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// How long a successor is published before it becomes active, so that resource servers caching JWKS
    /// already know it when the first token signed with it arrives.
    /// </summary>
    public TimeSpan PrepublishPeriod { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// How long a retired key stays available to validate and decrypt tokens issued before its retirement.
    /// It must cover the longest token lifetime, which is the absolute refresh token lifetime.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(90);

    /// <summary>How often each instance reloads the keys and applies the schedule.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    internal bool IsValid() =>
        RotationInterval > TimeSpan.Zero
        && PrepublishPeriod > TimeSpan.Zero
        && PrepublishPeriod < RotationInterval
        && RetentionPeriod > TimeSpan.Zero
        && RefreshInterval > TimeSpan.Zero
        && RefreshInterval < PrepublishPeriod;
}
