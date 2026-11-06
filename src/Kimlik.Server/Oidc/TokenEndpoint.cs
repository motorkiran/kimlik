using System.Collections.Immutable;
using System.Security.Claims;
using Kimlik.Application.Access;
using Kimlik.Application.Clients;
using Kimlik.Application.Mfa;
using Kimlik.Application.Organizations;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Kimlik.Security;
using Kimlik.Server.Identity;
using Kimlik.Server.Oidc.Dpop;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
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

        // A proof binds the tokens to the client's key (RFC 9449), and one that fails a check fails the request.
        var (proof, error) = await context.RequestServices.GetRequiredService<DpopProofValidator>().ValidateAsync(context.Request, accessToken: null, cancellationToken);
        if (error is not null)
        {
            return OidcResults.Forbid(DpopProof.InvalidProofError, error);
        }

        if (request.IsClientCredentialsGrantType())
        {
            return await IssueClientTokenAsync(context, request, proof, cancellationToken);
        }

        if (request.IsAuthorizationCodeGrantType() || request.IsDeviceCodeGrantType() || request.IsRefreshTokenGrantType() || request.IsTokenExchangeGrantType())
        {
            return await IssueUserTokenAsync(context, request, proof, cancellationToken);
        }

        throw new InvalidOperationException($"The grant type '{request.GrantType}' is not supported.");
    }

    /// <summary>A service client acts on its own behalf, so the token's subject is the client itself.</summary>
    private static async Task<IResult> IssueClientTokenAsync(HttpContext context, OpenIddictRequest request, DpopProof? proof, CancellationToken cancellationToken)
    {
        var services = context.RequestServices;
        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var scopes = services.GetRequiredService<IOpenIddictScopeManager>();

        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("The authenticated client cannot be found.");

        if (proof is null && ClientPresets.RequiresDpop(await applications.GetPropertiesAsync(application, cancellationToken)))
        {
            return DpopRequired();
        }

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, await applications.GetClientIdAsync(application, cancellationToken));
        identity.SetClaim(Claims.Name, await applications.GetDisplayNameAsync(application, cancellationToken));

        identity.SetScopes(request.GetScopes());
        identity.SetResources(KimlikAudience.Filter(await scopes.ListResourcesAsync(identity.GetScopes(), cancellationToken).ToListAsync(cancellationToken), identity.GetScopes()));

        var applicationId = Guid.Parse((await applications.GetIdAsync(application, cancellationToken))!);
        OidcPrincipalFactory.AddAccess(identity, await services.GetRequiredService<AccessResolver>().ForClientAsync(applicationId, cancellationToken));
        identity.SetDestinations(static _ => [Destinations.AccessToken]);

        if (proof is not null)
        {
            BindAccessToken(context, proof);
        }

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Exchanges an authorization code, a device code, a refresh token or, for a client calling other APIs on the user's
    /// behalf, a user's access token (RFC 8693). The account is checked again on every exchange, so suspended, locked-out
    /// or deleted users lose access as soon as their access token expires.
    /// </summary>
    private static async Task<IResult> IssueUserTokenAsync(HttpContext context, OpenIddictRequest request, DpopProof? proof, CancellationToken cancellationToken)
    {
        var services = context.RequestServices;
        var userManager = services.GetRequiredService<UserManager<User>>();

        var principal = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal
            ?? throw new InvalidOperationException("The authorization code or refresh token principal cannot be retrieved.");

        var exchange = request.IsTokenExchangeGrantType();
        if (exchange && RefuseExchange(request, principal) is { } refusal)
        {
            return refusal;
        }

        // A code or refresh token bound to a key is good only with a proof by it.
        if (principal.GetClaim(DpopBinding.ThumbprintClaim) is { } boundTo && !string.Equals(proof?.Thumbprint, boundTo, StringComparison.Ordinal))
        {
            return OidcResults.Forbid(DpopProof.InvalidProofError, "Send a DPoP proof by the key the code or refresh token is bound to.");
        }

        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("The authenticated client cannot be found.");
        if (proof is null && ClientPresets.RequiresDpop(await applications.GetPropertiesAsync(application, cancellationToken)))
        {
            return DpopRequired();
        }

        var user = await userManager.FindByIdAsync(principal.GetClaim(Claims.Subject)!);
        if (user is null || !user.CanSignIn || await userManager.IsLockedOutAsync(user)
            || !await services.GetRequiredService<SignInManager<User>>().CanSignInAsync(user))
        {
            return OidcResults.Forbid(Errors.InvalidGrant, "The token is no longer valid.");
        }

        // An impersonation never gets refresh tokens; one that somehow carries an actor is refused all the same.
        if (request.IsRefreshTokenGrantType() && OidcPrincipalFactory.GetActor(principal) is not null)
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

        // A second factor that the account or the organization requires now, but the session never had, as for a user
        // made an administrator since or a switch to an organization that requires one, takes signing in again.
        var methods = OidcPrincipalFactory.GetAuthenticationMethods(principal);
        if (!methods.Contains(SignInFlow.MultiFactorMethod, StringComparer.Ordinal)
            && (organization.Value?.RequireMfa == true || await services.GetRequiredService<MfaPolicy>().IsRequiredAsync(user.Id, cancellationToken)))
        {
            return OidcResults.Forbid(Errors.InvalidGrant, "A second factor is required. Sign in again.");
        }

        // A refresh carries the scopes of the sign-in, and OpenIddict only checks the scopes a request names, which a refresh
        // does not. One that an administrator took away from the client since, or deleted, ends the sign-in's tokens: the app
        // asks again, for what it may still have.
        if (request.IsRefreshTokenGrantType() && !await MayStillAskForAsync(applications, application, principal.GetScopes(), cancellationToken))
        {
            return OidcResults.Forbid(Errors.InvalidGrant, "The application may no longer ask for what this sign-in granted. Sign in again.");
        }

        var identity = await services.GetRequiredService<OidcPrincipalFactory>()
            .CreateAsync(
                user,
                exchange ? request.GetScopes() : principal.GetScopes(),
                exchange ? null : principal.GetResources(),
                authenticatedAt,
                methods,
                organization.Value?.Id,
                exchange ? null : principal.GetClaim(JwtRegisteredClaimNames.Sid),
                cancellationToken);

        if (exchange)
        {
            Delegate(identity, request.ClientId!, principal, services);
        }
        else
        {
            identity.SetAuthorizationId(principal.GetAuthorizationId());
        }

        // Tokens for an administrator acting as the user keep naming them, and expire with the impersonation.
        if (!exchange && OidcPrincipalFactory.GetActor(principal) is { } actor)
        {
            OidcPrincipalFactory.AddActor(identity, actor, principal.GetAccessTokenLifetime() ?? SignInFlow.ImpersonationLifetime);
        }

        if (proof is not null)
        {
            BindAccessToken(context, proof);

            // The refresh tokens of public clients are bound too; confidential clients authenticate to refresh (RFC 9449, section 5).
            if (await applications.HasClientTypeAsync(application, ClientTypes.Public, cancellationToken))
            {
                identity.SetClaim(DpopBinding.ThumbprintClaim, proof.Thumbprint);
            }
        }

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>Binds the access token being issued to the key of the request's proof; see <see cref="DpopServerHandlers"/>.</summary>
    private static void BindAccessToken(HttpContext context, DpopProof proof) =>
        context.Features.Get<OpenIddictServerAspNetCoreFeature>()!.Transaction!.Properties[DpopBinding.ThumbprintProperty] = proof.Thumbprint;

    private static IResult DpopRequired() => OidcResults.Forbid(DpopProof.InvalidProofError, "This client must send a DPoP proof (RFC 9449) with its token requests.");

    /// <summary>Whether the client may still ask for every one of <paramref name="scopes"/>; <c>openid</c> and <c>offline_access</c> take no permission.</summary>
    private static async Task<bool> MayStillAskForAsync(IOpenIddictApplicationManager applications, object application, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        foreach (var scope in scopes)
        {
            if (scope is not (Scopes.OpenId or Scopes.OfflineAccess) && !await applications.HasPermissionAsync(application, Permissions.Prefixes.Scope + scope, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Refuses exchanges Kimlik does not make: other subject tokens than a user's access token, actor tokens, and scopes
    /// other than those of APIs, Kimlik's own included, which would carry all of the user's access to it.
    /// </summary>
    private static IResult? RefuseExchange(OpenIddictRequest request, ClaimsPrincipal subjectToken)
    {
        if (request.SubjectTokenType != TokenTypeIdentifiers.AccessToken || !string.IsNullOrEmpty(request.ActorToken))
        {
            return OidcResults.Forbid(Errors.InvalidRequest, "Only a user's access token can be exchanged, without an actor token.");
        }

        // A service client acting on its own behalf is the subject of its tokens (see IssueClientTokenAsync).
        if (subjectToken.GetClaim(Claims.Subject) == subjectToken.GetClaim(Claims.ClientId))
        {
            return OidcResults.Forbid(Errors.InvalidGrant, "Only a user's access token can be exchanged.");
        }

        var scopes = request.GetScopes();
        return scopes.IsEmpty || scopes.Any(scope => scope is Scopes.OpenId or Scopes.Profile or Scopes.Email or Scopes.Phone or Scopes.OfflineAccess || KimlikScopes.IsReserved(scope))
            ? OidcResults.Forbid(Errors.InvalidScope, "Ask for the scopes of the APIs to call, other than Kimlik's own.")
            : null;
    }

    /// <summary>
    /// Makes the identity a token the client holds for the user: <c>act</c> names it, there is only an access token, and
    /// it expires no later than the token it came from.
    /// </summary>
    private static void Delegate(ClaimsIdentity identity, string clientId, ClaimsPrincipal subjectToken, IServiceProvider services)
    {
        OidcPrincipalFactory.AddDelegation(identity, clientId, subjectToken);
        identity.SetDestinations(claim => claim.GetDestinations().Contains(Destinations.AccessToken) ? [Destinations.AccessToken] : []);

        var remaining = subjectToken.GetExpirationDate() - services.GetRequiredService<TimeProvider>().GetUtcNow();
        if (remaining < services.GetRequiredService<IOptions<TokenOptions>>().Value.AccessTokenLifetime)
        {
            identity.SetAccessTokenLifetime(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// The organization the new tokens act in: the one a refresh token request switches to, or else the one of the
    /// code or refresh token. Membership is checked again either way, so removed members lose the context.
    /// </summary>
    private static async Task<Result<OrganizationResponse?>> ResolveOrganizationAsync(
        IServiceProvider services, OpenIddictRequest request, ClaimsPrincipal principal, User user, CancellationToken cancellationToken)
    {
        var reference = request.IsRefreshTokenGrantType() && request.GetParameter(KimlikParameters.Organization)?.ToString() is { Length: > 0 } switchTo
            ? switchTo
            : OidcPrincipalFactory.GetOrganizationId(principal)?.ToString();

        if (reference is null)
        {
            return (OrganizationResponse?)null;
        }

        var organization = await services.GetRequiredService<UserOrganizations>().FindAsync(user.Id, reference, cancellationToken);
        return organization.IsSuccess ? organization.Value : organization.Error;
    }
}
