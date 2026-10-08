using System.Text.RegularExpressions;
using Kimlik.Contracts;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Application.ApiResources;

/// <summary>The formats of API resource scopes and audiences.</summary>
internal static partial class ApiResourceNames
{
    /// <summary>Scopes with a meaning in OpenID Connect, and Kimlik's own API.</summary>
    private static readonly HashSet<string> ReservedScopes = new(StringComparer.Ordinal)
    {
        Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Phone, Scopes.Address, Scopes.OfflineAccess, Scopes.Roles, KimlikScopes.Api,
    };

    public static bool IsValidScope(string scope) => ScopePattern().IsMatch(scope);

    public static bool IsReservedScope(string scope) => ReservedScopes.Contains(scope);

    /// <summary>The characters OAuth allows in a scope token (RFC 6749, section 3.3), which audiences share here.</summary>
    public static bool IsValidAudience(string audience) => AudiencePattern().IsMatch(audience);

    [GeneratedRegex("^[a-z][a-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ScopePattern();

    [GeneratedRegex(@"^[\x21\x23-\x5B\x5D-\x7E]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AudiencePattern();
}
