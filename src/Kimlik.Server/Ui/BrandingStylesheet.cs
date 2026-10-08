using System.Security.Cryptography;
using System.Text;
using Kimlik.Application.Branding;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Ui;

/// <summary>
/// The custom stylesheet of the hosted pages, from <see cref="BrandingOptions.StylesheetPath"/>. Kimlik serves it itself,
/// so the content security policy allows it, at a URL that changes with its content, so browsers can keep it for good.
/// </summary>
internal sealed class BrandingStylesheet
{
    public const string Path = "/branding.css";

    public BrandingStylesheet(IOptions<BrandingOptions> branding)
    {
        if (branding.Value.StylesheetPath is { Length: > 0 } path)
        {
            Content = File.ReadAllText(path);
            Url = $"{Path}?v={Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Content)))[..12]}";
        }
    }

    public string? Content { get; }

    /// <summary>Where the pages load it from, relative to the site, or <see langword="null"/> when there is none.</summary>
    public string? Url { get; }
}
