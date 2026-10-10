using Kimlik.Server.SocialLogin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages;

/// <summary>Kimlik's SAML service provider metadata, which providers import to set up a SAML app.</summary>
public sealed class SamlMetadataModel(SamlServiceProvider saml) : PageModel
{
    public ContentResult OnGet() => Content(saml.Metadata(), "application/samlmetadata+xml");
}
