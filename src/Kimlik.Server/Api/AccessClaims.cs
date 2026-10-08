using System.Security.Claims;
using Kimlik.Contracts;

namespace Kimlik.Server.Api;

/// <summary>Reads the access claims that <see cref="Oidc.OidcPrincipalFactory.AddAccess"/> writes into access tokens.</summary>
internal static class AccessClaims
{
    /// <summary>
    /// The permissions of a validated access token. The token holds them as a JSON array, which token
    /// validation turns into one claim per permission.
    /// </summary>
    public static IReadOnlySet<string> GetPermissions(ClaimsPrincipal principal) =>
        principal.FindAll(KimlikClaimTypes.Permissions).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);
}
