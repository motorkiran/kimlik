using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using Kimlik.Domain.Users;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Oidc;

/// <summary>
/// Builds the identity behind a user's tokens from the current state of the account, so every code
/// exchange and refresh reflects profile changes.
/// </summary>
public sealed class OidcPrincipalFactory(IOpenIddictScopeManager scopes)
{
    public async Task<ClaimsIdentity> CreateAsync(
        User user, ImmutableArray<string> grantedScopes, DateTimeOffset? authenticatedAt, CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.Name)
            .SetClaim(Claims.GivenName, user.GivenName)
            .SetClaim(Claims.FamilyName, user.FamilyName)
            .SetClaim(Claims.Locale, user.Locale);

        identity.AddClaim(new Claim(Claims.EmailVerified, user.EmailConfirmed ? "true" : "false", ClaimValueTypes.Boolean));
        identity.AddClaim(UnixTimeClaim(Claims.UpdatedAt, user.UpdatedAt));

        if (authenticatedAt is not null)
        {
            identity.AddClaim(UnixTimeClaim(Claims.AuthenticationTime, authenticatedAt.Value));
        }

        identity.SetScopes(grantedScopes);
        identity.SetResources(await scopes.ListResourcesAsync(grantedScopes, cancellationToken).ToListAsync(cancellationToken));
        identity.SetDestinations(GetDestinations);

        return identity;
    }

    /// <summary>When the user authenticated, as carried by a previously issued token.</summary>
    public static DateTimeOffset? GetAuthenticationTime(ClaimsPrincipal principal) =>
        long.TryParse(principal.GetClaim(Claims.AuthenticationTime), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    /// <summary>Profile and email claims only leave Kimlik when the client was granted the matching scope.</summary>
    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        var identity = claim.Subject!;

        return claim.Type switch
        {
            Claims.Subject or Claims.AuthenticationTime => [Destinations.AccessToken, Destinations.IdentityToken],

            Claims.Name or Claims.GivenName or Claims.FamilyName or Claims.Locale or Claims.UpdatedAt when identity.HasScope(Scopes.Profile)
                => [Destinations.AccessToken, Destinations.IdentityToken],

            Claims.Email or Claims.EmailVerified when identity.HasScope(Scopes.Email)
                => [Destinations.AccessToken, Destinations.IdentityToken],

            _ => [],
        };
    }

    private static Claim UnixTimeClaim(string type, DateTimeOffset value) =>
        new(type, value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64);
}
