using System.Text.RegularExpressions;
using Kimlik.Contracts;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Application.ApiResources;

/// <summary>The formats of API resource scopes and audiences.</summary>
internal static partial class ApiResourceNames
{
    /// <summary>Scopes with a meaning in OpenID Connect; Kimlik's own are reserved too (<see cref="KimlikScopes.IsReserved"/>).</summary>
    private static readonly HashSet<string> ReservedScopes = new(StringComparer.Ordinal)
    {
        Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Phone, Scopes.Address, Scopes.OfflineAccess, Scopes.Roles,
    };

    public static bool IsValidScope(string scope) => ScopePattern().IsMatch(scope);

    public static bool IsReservedScope(string scope) => ReservedScopes.Contains(scope) || KimlikScopes.IsReserved(scope);

    /// <summary>
    /// Kimlik's own audiences, whose tokens reach Kimlik's API, with the user's system permissions for <c>kimlik</c>: only
    /// their own scopes may name them, or a scope that anyone allowed to register APIs creates would lead there.
    /// </summary>
    public static bool IsReservedAudience(string audience) => KimlikScopes.IsReserved(audience);

    /// <summary>The characters OAuth allows in a scope token (RFC 6749, section 3.3), which audiences share here.</summary>
    public static bool IsValidAudience(string audience) => AudiencePattern().IsMatch(audience);

    [GeneratedRegex("^[a-z][a-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ScopePattern();

    [GeneratedRegex(@"^[\x21\x23-\x5B\x5D-\x7E]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AudiencePattern();
}
