using System.Security.Claims;
using Kimlik.Application.Access;
using Kimlik.Application.Organizations;
using Kimlik.Contracts;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Oidc;

/// <summary>
/// Issues tokens once OpenIddict has validated the request: the client is authenticated and allowed to use
/// the grant and the requested scopes, and codes and refresh tokens are valid, before this code runs.
/// </summary>
internal static class TokenEndpoint
{
    public static IEndpointRouteBuilder MapTokenEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("connect/token", ExchangeAsync).ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> ExchangeAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsClientCredentialsGrantType())
        {
            return await IssueClientTokenAsync(request, context.RequestServices, cancellationToken);
        }

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            return await IssueUserTokenAsync(context, request, cancellationToken);
        }

        throw new InvalidOperationException($"The grant type '{request.GrantType}' is not supported.");
    }

    /// <summary>A service client acts on its own behalf, so the token's subject is the client itself.</summary>
    private static async Task<IResult> IssueClientTokenAsync(OpenIddictRequest request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var scopes = services.GetRequiredService<IOpenIddictScopeManager>();

        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("The authenticated client cannot be found.");

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, await applications.GetClientIdAsync(application, cancellationToken));
        identity.SetClaim(Claims.Name, await applications.GetDisplayNameAsync(application, cancellationToken));

        identity.SetScopes(request.GetScopes());
        identity.SetResources(await scopes.ListResourcesAsync(identity.GetScopes(), cancellationToken).ToListAsync(cancellationToken));

        var applicationId = Guid.Parse((await applications.GetIdAsync(application, cancellationToken))!);
        OidcPrincipalFactory.AddAccess(identity, await services.GetRequiredService<AccessResolver>().ForClientAsync(applicationId, cancellationToken));
        identity.SetDestinations(static _ => [Destinations.AccessToken]);

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Exchanges an authorization code or a refresh token. The account is checked again on every exchange,
    /// so suspended, locked-out or deleted users lose access as soon as their access token expires.
    /// </summary>
    private static async Task<IResult> IssueUserTokenAsync(HttpContext context, OpenIddictRequest request, CancellationToken cancellationToken)
    {
        var services = context.RequestServices;
        var userManager = services.GetRequiredService<UserManager<User>>();

        var principal = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal
            ?? throw new InvalidOperationException("The authorization code or refresh token principal cannot be retrieved.");

        var user = await userManager.FindByIdAsync(principal.GetClaim(Claims.Subject)!);
        if (user is null || !user.CanSignIn || await userManager.IsLockedOutAsync(user)
            || !await services.GetRequiredService<SignInManager<User>>().CanSignInAsync(user))
        {
            return OidcResults.Forbid(Errors.InvalidGrant, "The token is no longer valid.");
        }

        var authenticatedAt = OidcPrincipalFactory.GetAuthenticationTime(principal);
        var absoluteLifetime = services.GetRequiredService<IOptions<TokenOptions>>().Value.RefreshTokenAbsoluteLifetime;
        if (request.IsRefreshTokenGrantType()
            && (authenticatedAt is null || authenticatedAt.Value + absoluteLifetime <= services.GetRequiredService<TimeProvider>().GetUtcNow()))
        {
            return OidcResults.Forbid(Errors.InvalidGrant, "The session has expired. Sign in again.");
        }

        var organization = await ResolveOrganizationAsync(services, request, principal, user, cancellationToken);
        if (organization.IsFailure)
        {
            return OidcResults.Forbid(Errors.InvalidGrant, organization.Error.Message);
        }

        var identity = await services.GetRequiredService<OidcPrincipalFactory>()
            .CreateAsync(
                user,
                principal.GetScopes(),
                principal.GetResources(),
                authenticatedAt,
                OidcPrincipalFactory.GetAuthenticationMethods(principal),
                organization.Value,
                cancellationToken);
        identity.SetAuthorizationId(principal.GetAuthorizationId());

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// The organization the new tokens act in: the one a refresh token request switches to, or else the one of the
    /// code or refresh token. Membership is checked again either way, so removed members lose the context.
    /// </summary>
    private static async Task<Result<Guid?>> ResolveOrganizationAsync(
        IServiceProvider services, OpenIddictRequest request, ClaimsPrincipal principal, User user, CancellationToken cancellationToken)
    {
        var reference = request.IsRefreshTokenGrantType() && request.GetParameter(KimlikParameters.Organization)?.ToString() is { Length: > 0 } switchTo
            ? switchTo
            : OidcPrincipalFactory.GetOrganizationId(principal)?.ToString();

        if (reference is null)
        {
            return (Guid?)null;
        }

        var organization = await services.GetRequiredService<UserOrganizations>().FindAsync(user.Id, reference, cancellationToken);
        return organization.IsSuccess ? organization.Value.Id : organization.Error;
    }
}
