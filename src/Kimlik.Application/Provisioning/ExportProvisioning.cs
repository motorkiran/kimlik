using Kimlik.Application.Abstractions;
using Kimlik.Application.ApiResources;
using Kimlik.Application.Clients;
using Kimlik.Application.Roles;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Kimlik.Application.Provisioning;

/// <summary>
/// Describes the access model as a provisioning document, which applies back to the same state. What Kimlik
/// defines itself is left out, and so are client secrets.
/// </summary>
public sealed class ExportProvisioningHandler(IKimlikDbContext context, IOpenIddictScopeManager scopes, IOpenIddictApplicationManager applications)
{
    public async Task<ProvisioningDocument> HandleAsync(CancellationToken cancellationToken)
    {
        var permissions = await context.Permissions.AsNoTracking()
            .Where(permission => !permission.IsSystem)
            .OrderBy(permission => permission.Key)
            .Select(permission => new ProvisionedPermission { Key = permission.Key, Description = permission.Description })
            .ToListAsync(cancellationToken);

        var roleEntities = await context.Roles.AsNoTracking().Where(role => !role.IsSystem).OrderBy(role => role.Key).ToListAsync(cancellationToken);
        var rolePermissions = await context.PermissionsOfAsync([.. roleEntities.Select(role => role.Id)], cancellationToken);
        var roles = roleEntities.Select(role => new ProvisionedRole
        {
            Key = role.Key,
            Name = role.Name,
            Description = role.Description,
            Scope = role.Scope.ToContract(),
            Permissions = [.. rolePermissions[role.Id].Order(StringComparer.Ordinal)],
        }).ToList();

        var apiResources = new List<ProvisionedApiResource>();
        foreach (var scope in await context.Scopes.AsNoTracking().Where(scope => scope.Name != KimlikScopes.Api).OrderBy(scope => scope.Name).ToListAsync(cancellationToken))
        {
            var resource = await scopes.ToResponseAsync(scope, cancellationToken);
            apiResources.Add(new ProvisionedApiResource
            {
                Scope = resource.Scope,
                Audience = resource.Audience,
                DisplayName = resource.DisplayName,
                Description = resource.Description,
            });
        }

        var clientEntities = await context.Applications.AsNoTracking().OrderBy(application => application.ClientId).ToListAsync(cancellationToken);
        var clientRoles = await context.RolesOfClientsAsync([.. clientEntities.Select(application => application.Id)], cancellationToken);
        var clients = new List<ProvisionedClient>();
        foreach (var application in clientEntities)
        {
            var client = await applications.ToResponseAsync(application, clientRoles[application.Id], cancellationToken);
            clients.Add(new ProvisionedClient
            {
                ClientId = client.ClientId,
                DisplayName = client.DisplayName ?? client.ClientId,
                Type = client.Type,
                FirstParty = client.FirstParty,
                RedirectUris = client.RedirectUris,
                PostLogoutRedirectUris = client.PostLogoutRedirectUris,
                Scopes = client.Scopes,
                Roles = client.Type == ClientType.Service ? client.Roles : null,
                RequireOrganization = client.RequireOrganization,
            });
        }

        return new ProvisioningDocument { Permissions = permissions, Roles = roles, ApiResources = apiResources, Clients = clients };
    }
}
