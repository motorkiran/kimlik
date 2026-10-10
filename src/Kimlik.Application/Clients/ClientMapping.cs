using Kimlik.Application.Abstractions;
using Kimlik.Contracts.Management;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using OidcPermissions = OpenIddict.Abstractions.OpenIddictConstants.Permissions;

namespace Kimlik.Application.Clients;

internal static class ClientMapping
{
    public static async Task<ClientResponse> ToResponseAsync(
        this IOpenIddictApplicationManager applications, object application, IEnumerable<string> roles, CancellationToken cancellationToken)
    {
        var permissions = await applications.GetPermissionsAsync(application, cancellationToken);
        var type = await applications.GetPresetAsync(application, cancellationToken);

        return new ClientResponse(
            await applications.GetIdentifierAsync(application, cancellationToken),
            (await applications.GetClientIdAsync(application, cancellationToken))!,
            await applications.GetDisplayNameAsync(application, cancellationToken),
            type,
            FirstParty: await applications.GetConsentTypeAsync(application, cancellationToken) == ConsentTypes.Implicit,
            [.. (await applications.GetRedirectUrisAsync(application, cancellationToken)).Order(StringComparer.Ordinal)],
            [.. (await applications.GetPostLogoutRedirectUrisAsync(application, cancellationToken)).Order(StringComparer.Ordinal)],
            ClientPresets.ScopesOf(type, permissions),
            [.. roles.Order(StringComparer.Ordinal)],
            ClientPresets.RequiresOrganization(await applications.GetPropertiesAsync(application, cancellationToken)),
            (await applications.GetRequirementsAsync(application, cancellationToken)).Contains(Requirements.Features.PushedAuthorizationRequests),
            await applications.GetJsonWebKeySetAsync(application, cancellationToken) is { } keys ? ClientKeys.ToJson(keys) : null,
            permissions.Contains(OidcPermissions.GrantTypes.TokenExchange, StringComparer.Ordinal));
    }

    public static async Task<ClientResponse> ToResponseAsync(
        this IOpenIddictApplicationManager applications, IKimlikDbContext context, object application, CancellationToken cancellationToken)
    {
        var id = await applications.GetIdentifierAsync(application, cancellationToken);
        return await applications.ToResponseAsync(application, (await context.RolesOfClientsAsync([id], cancellationToken))[id], cancellationToken);
    }

    /// <summary>The ID of a client as Kimlik uses it, for example in role assignments.</summary>
    public static async Task<Guid> GetIdentifierAsync(this IOpenIddictApplicationManager applications, object application, CancellationToken cancellationToken) =>
        Guid.Parse((await applications.GetIdAsync(application, cancellationToken))!);

    /// <summary>The global role keys of each of the given clients.</summary>
    public static async Task<ILookup<Guid, string>> RolesOfClientsAsync(this IKimlikDbContext context, IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken)
    {
        var assignments = await context.ClientRoles
            .Where(assignment => applicationIds.Contains(assignment.ApplicationId))
            .Join(context.Roles, assignment => assignment.RoleId, role => role.Id, (assignment, role) => new { assignment.ApplicationId, role.Key })
            .ToListAsync(cancellationToken);

        return assignments.ToLookup(assignment => assignment.ApplicationId, assignment => assignment.Key);
    }

    /// <summary>The Kimlik client type, read from the OpenIddict settings it produced.</summary>
    public static async Task<ClientType> GetPresetAsync(this IOpenIddictApplicationManager applications, object application, CancellationToken cancellationToken) =>
        ClientPresets.TypeOf(
            await applications.GetClientTypeAsync(application, cancellationToken),
            await applications.GetApplicationTypeAsync(application, cancellationToken),
            await applications.GetPermissionsAsync(application, cancellationToken));
}
