using System.Security.Claims;
using Kimlik.Contracts;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Kimlik.Server.Api;

/// <summary>Marks an endpoint with the system permissions it requires; also read by the OpenAPI document.</summary>
internal sealed record RequiredPermission(IReadOnlyList<string> Permissions);

/// <summary>Marks an endpoint of the Account API, which acts for the signed-in user; also read by the OpenAPI document.</summary>
internal sealed record RequiredSignedInUser(IReadOnlyList<string> Audiences);

internal sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (AccessClaims.GetPermissions(context.User).Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal static class PermissionEndpointExtensions
{
    /// <summary>
    /// Requires an access token issued to a user, rather than to a service client acting on its own behalf, for one of
    /// <paramref name="audiences"/>: Kimlik's API (<c>kimlik</c>) when none are given.
    /// </summary>
    public static TBuilder RequireSignedInUser<TBuilder>(this TBuilder builder, params string[] audiences)
        where TBuilder : IEndpointConventionBuilder
    {
        string[] accepted = audiences.Length > 0 ? audiences : [KimlikScopes.Api];
        var policy = new AuthorizationPolicyBuilder(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => AccountCaller.UserId(context.User) is not null && Array.Exists(accepted, audience => IsFor(context.User, audience)))
            .Build();

        return builder.RequireAuthorization(policy).WithMetadata(new RequiredSignedInUser(accepted));
    }

    /// <summary>Whether the token is for one of Kimlik's audiences, each of which goes with the scope of the same name.</summary>
    private static bool IsFor(ClaimsPrincipal principal, string audience) => principal.HasAudience(audience) && principal.HasScope(audience);

    /// <summary>Requires an access token for Kimlik's API that carries every one of <paramref name="permissions"/>.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        var policy = new AuthorizationPolicyBuilder(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => IsFor(context.User, KimlikScopes.Api))
            .AddRequirements([.. permissions.Select(permission => new PermissionRequirement(permission))])
            .Build();

        return builder.RequireAuthorization(policy).WithMetadata(new RequiredPermission(permissions));
    }
}
