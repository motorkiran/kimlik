using System.ComponentModel.DataAnnotations;

namespace Kimlik.Application.Bootstrap;

/// <summary>The first administrator, from the <c>Kimlik:Bootstrap</c> configuration section.</summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Kimlik:Bootstrap";

    [EmailAddress]
    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }

    public bool IsValid() => string.IsNullOrEmpty(AdminEmail) == string.IsNullOrEmpty(AdminPassword);
}
