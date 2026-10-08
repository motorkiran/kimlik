namespace Kimlik.Infrastructure.Provisioning;

/// <summary>From the <c>Kimlik:Provisioning</c> configuration section.</summary>
public sealed class ProvisioningOptions
{
    public const string SectionName = "Kimlik:Provisioning";

    /// <summary>A provisioning document to apply whenever the database is prepared, such as at startup.</summary>
    public string? FilePath { get; set; }

    public bool IsValid() => string.IsNullOrEmpty(FilePath) || File.Exists(FilePath);
}
