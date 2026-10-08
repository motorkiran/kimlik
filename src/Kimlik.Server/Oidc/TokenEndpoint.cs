using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Oidc;

/// <summary>
/// Issues tokens once OpenIddict has validated the request: the client is authenticated and allowed to use
/// the grant and the requested scopes before this code runs.
/// </summary>
internal static class TokenEndpoint
{
    public static IEndpointRouteBuilder MapTokenEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("connect/token", ExchangeAsync).ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> ExchangeAsync(
        HttpContext context,
        IOpenIddictApplicationManager applications,
        IOpenIddictScopeManager scopes,
        CancellationToken cancellationToken)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsClientCredentialsGrantType())
        {
            return await IssueClientTokenAsync(request, applications, scopes, cancellationToken);
        }

        throw new InvalidOperationException($"The grant type '{request.GrantType}' is not supported.");
    }

    /// <summary>A service client acts on its own behalf, so the token's subject is the client itself.</summary>
    private static async Task<IResult> IssueClientTokenAsync(
        OpenIddictRequest request,
        IOpenIddictApplicationManager applications,
        IOpenIddictScopeManager scopes,
        CancellationToken cancellationToken)
    {
        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("The authenticated client cannot be found.");

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, await applications.GetClientIdAsync(application, cancellationToken));
        identity.SetClaim(Claims.Name, await applications.GetDisplayNameAsync(application, cancellationToken));

        identity.SetScopes(request.GetScopes());
        identity.SetResources(await scopes.ListResourcesAsync(identity.GetScopes(), cancellationToken).ToListAsync(cancellationToken));
        identity.SetDestinations(static _ => [Destinations.AccessToken]);

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
