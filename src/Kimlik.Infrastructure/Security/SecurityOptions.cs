namespace Kimlik.Infrastructure.Security;

/// <summary>Security settings from the <c>Kimlik:Security</c> configuration section.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Kimlik:Security";

    public const int MasterKeySizeInBytes = 32;

    /// <summary>
    /// Base64-encoded 256-bit key that encrypts secrets at rest. Generate one with
    /// <c>openssl rand -base64 32</c> and back it up: encrypted data cannot be recovered without it.
    /// </summary>
    public string MasterKey { get; set; } = string.Empty;

    internal static bool IsValidMasterKey(string? value)
    {
        Span<byte> buffer = stackalloc byte[MasterKeySizeInBytes + 1];
        return Convert.TryFromBase64String(value ?? string.Empty, buffer, out var length) && length == MasterKeySizeInBytes;
    }
}
