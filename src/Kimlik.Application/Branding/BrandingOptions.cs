using System.ComponentModel.DataAnnotations;

namespace Kimlik.Application.Branding;

/// <summary>Look of the hosted pages and emails, from the <c>Kimlik:Branding</c> configuration section.</summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Kimlik:Branding";

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string ProductName { get; set; } = "Kimlik";

    /// <summary>Absolute URL of a logo shown above the forms and in emails; the product name is shown when unset.</summary>
    public Uri? LogoUrl { get; set; }

    /// <summary>Accent color as <c>#rrggbb</c>.</summary>
    [RegularExpression("^#[0-9a-fA-F]{6}$")]
    public string PrimaryColor { get; set; } = "#4f46e5";
}
