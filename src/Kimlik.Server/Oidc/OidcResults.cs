using Microsoft.AspNetCore.Authentication;
using OpenIddict.Server.AspNetCore;

namespace Kimlik.Server.Oidc;

/// <summary>Protocol error responses written by OpenIddict.</summary>
internal static class OidcResults
{
    public static AuthenticationProperties ErrorProperties(string error, string description) => new(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
    });

    public static IResult Forbid(string error, string description) =>
        Results.Forbid(ErrorProperties(error, description), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
