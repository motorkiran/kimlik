using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Oidc;

/// <summary>Returns the claims of the user behind an access token, limited to the scopes it was granted.</summary>
internal static class UserInfoEndpoint
{
    public static IEndpointRouteBuilder MapUserInfoEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("connect/userinfo", [HttpMethods.Get, HttpMethods.Post], GetUserInfoAsync).ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> GetUserInfoAsync(HttpContext context, UserManager<User> userManager)
    {
        var principal = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal
            ?? throw new InvalidOperationException("The access token principal cannot be retrieved.");

        var user = await userManager.FindByIdAsync(principal.GetClaim(Claims.Subject)!);
        if (user is null || !user.CanSignIn)
        {
            return Results.Challenge(
                OidcResults.ErrorProperties(Errors.InvalidToken, "The access token is no longer valid."),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var claims = new Dictionary<string, object?>(StringComparer.Ordinal) { [Claims.Subject] = user.Id.ToString() };

        if (principal.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.Name;
            claims[Claims.GivenName] = user.GivenName;
            claims[Claims.FamilyName] = user.FamilyName;
            claims[Claims.Locale] = user.Locale;
            claims[Claims.Picture] = user.PictureUrl;
            claims[Claims.Zoneinfo] = user.TimeZone;
            claims[Claims.UpdatedAt] = user.UpdatedAt.ToUnixTimeSeconds();
        }

        if (principal.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        // Unknown values are omitted rather than returned as null (OpenID Connect Core 1.0, section 5.3.2).
        return Results.Ok(claims.Where(claim => claim.Value is not null).ToDictionary(StringComparer.Ordinal));
    }
}
