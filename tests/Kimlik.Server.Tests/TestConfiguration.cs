using System.Security.Cryptography;

namespace Kimlik.Server.Tests;

/// <summary>Configuration shared by every in-memory Kimlik host in the tests.</summary>
internal static class TestConfiguration
{
    public const string Environment = "Testing";

    /// <summary>The base address of the in-memory test server, which is also the token issuer.</summary>
    public const string PublicUrl = "http://localhost/";

    /// <summary>A fresh master key per test run; nothing encrypted by the tests outlives it.</summary>
    public static readonly string MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static Dictionary<string, string?> Create(string connectionString) => new()
    {
        ["ConnectionStrings:Kimlik"] = connectionString,
        ["Kimlik:Server:PublicUrl"] = PublicUrl,
        ["Kimlik:Server:RequireHttps"] = "false",
        ["Kimlik:Security:MasterKey"] = MasterKey,
        // Any reuse of a refresh token counts as theft, so the tests need not wait out the leeway.
        ["Kimlik:Tokens:RefreshTokenReuseLeeway"] = "00:00:00",
        ["Kimlik:Outbox:PollingInterval"] = "00:00:00.200",
        // Webhooks retry three times, quickly, so failures and retries fit in a test.
        ["Kimlik:Webhooks:PollingInterval"] = "00:00:00.200",
        ["Kimlik:Webhooks:RetryDelays:0"] = "00:00:00.200",
        ["Kimlik:Webhooks:RetryDelays:1"] = "00:00:00.200",
        ["Kimlik:Webhooks:RetryDelays:2"] = "00:00:00.200",
        // Tests sign administrators in with a password alone; the MFA tests turn the policy on where they need it.
        ["Kimlik:Mfa:RequireForAdministrators"] = "false",
        // Tests never reach the internet; the breached password tests fake the service.
        ["Kimlik:Accounts:BreachedPasswordCheck"] = "false",
        // Every test request comes from the same address; rate limiting has its own tests.
        ["Kimlik:RateLimits:SignInsPerMinute"] = "100000",
        ["Kimlik:RateLimits:SignUpsPerMinute"] = "100000",
        ["Kimlik:RateLimits:EmailRequestsPerMinute"] = "100000",
        ["Kimlik:RateLimits:ProtocolRequestsPerMinute"] = "100000",
    };
}
