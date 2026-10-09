using Kimlik.Application.Abstractions;
using Kimlik.Application.Clients;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Application.Provisioning;

public sealed class ClientProvisioner(
    IKimlikDbContext context,
    IOpenIddictApplicationManager applications,
    CreateClientHandler create,
    UpdateClientHandler update,
    SetClientRolesHandler setRoles,
    RegenerateClientSecretHandler replaceSecret)
{
    public async Task<Result<ProvisioningChange>> ApplyAsync(ProvisionedClient declared, CancellationToken cancellationToken)
    {
        if (declared.ClientSecret is not null && declared.JsonWebKeySet is not null)
        {
            return ClientErrors.SecretOrKeys;
        }

        if (await applications.FindByClientIdAsync(declared.ClientId, cancellationToken) is not { } application)
        {
            var created = await create.CreateAsync(
                new CreateClientRequest
                {
                    ClientId = declared.ClientId,
                    DisplayName = declared.DisplayName,
                    Type = declared.Type,
                    FirstParty = declared.FirstParty,
                    RedirectUris = declared.RedirectUris,
                    PostLogoutRedirectUris = declared.PostLogoutRedirectUris,
                    Scopes = declared.Scopes,
                    Roles = declared.Roles ?? [],
                    RequireOrganization = declared.RequireOrganization,
                    RequirePushedAuthorization = declared.RequirePushedAuthorization,
                    JsonWebKeySet = declared.JsonWebKeySet,
                },
                declared.ClientSecret,
                cancellationToken);
            return created.IsSuccess ? ProvisioningChange.Created : created.Error;
        }

        var existing = await applications.ToResponseAsync(context, application, cancellationToken);
        if (existing.Type != declared.Type)
        {
            return ProvisioningErrors.FixedProperty("type");
        }

        var change = ProvisioningChange.Unchanged;

        if (!HasSettings(existing, declared))
        {
            var request = new UpdateClientRequest
            {
                DisplayName = declared.DisplayName,
                FirstParty = declared.FirstParty,
                RedirectUris = declared.RedirectUris,
                PostLogoutRedirectUris = declared.PostLogoutRedirectUris,
                Scopes = declared.Scopes,
                RequireOrganization = declared.RequireOrganization,
                RequirePushedAuthorization = declared.RequirePushedAuthorization,
            };
            var updated = await update.HandleAsync(
                existing.Id, declared.JsonWebKeySet is { } keys ? request with { JsonWebKeySet = keys } : request, cancellationToken);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            change = ProvisioningChange.Updated;
        }

        if (declared.Roles is not null && !Declared.SameSet(existing.Roles, declared.Roles))
        {
            var replaced = await setRoles.HandleAsync(existing.Id, new SetRolesRequest { Roles = declared.Roles }, cancellationToken);
            if (replaced.IsFailure)
            {
                return replaced.Error;
            }

            change = ProvisioningChange.Updated;
        }

        if (declared.ClientSecret is { } secret && !await IsCurrentSecretAsync(application, existing.Type, secret, cancellationToken))
        {
            var replaced = await replaceSecret.ReplaceAsync(existing.Id, secret, cancellationToken);
            if (replaced.IsFailure)
            {
                return replaced.Error;
            }

            change = ProvisioningChange.Updated;
        }

        return change;
    }

    /// <summary>
    /// Whether the client already has the declared settings, and keys if any are declared; <c>openid</c> is implied for apps
    /// that sign users in.
    /// </summary>
    private static bool HasSettings(ClientResponse existing, ProvisionedClient declared) =>
        existing.DisplayName == declared.DisplayName.Trim()
        && existing.FirstParty == declared.FirstParty
        && existing.RequireOrganization == declared.RequireOrganization
        && existing.RequirePushedAuthorization == declared.RequirePushedAuthorization
        && (declared.JsonWebKeySet is null || ClientKeys.AreSame(existing.JsonWebKeySet, declared.JsonWebKeySet))
        && Declared.SameUris(existing.RedirectUris, declared.RedirectUris)
        && Declared.SameUris(existing.PostLogoutRedirectUris, declared.PostLogoutRedirectUris)
        && Declared.SameSet(existing.Scopes.Where(scope => scope != Scopes.OpenId), declared.Scopes.Where(scope => scope != Scopes.OpenId));

    private async Task<bool> IsCurrentSecretAsync(object application, ClientType type, string secret, CancellationToken cancellationToken) =>
        ClientPresets.IsConfidential(type) && await applications.ValidateClientSecretAsync(application, secret, cancellationToken);
}
