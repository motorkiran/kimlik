using Kimlik.Application.ApiResources;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using OpenIddict.Abstractions;

namespace Kimlik.Application.Provisioning;

public sealed class ApiResourceProvisioner(
    IOpenIddictScopeManager scopes,
    CreateApiResourceHandler create,
    UpdateApiResourceHandler update)
{
    public async Task<Result<ProvisioningChange>> ApplyAsync(ProvisionedApiResource declared, CancellationToken cancellationToken)
    {
        if (await scopes.FindByNameAsync(declared.Scope, cancellationToken) is not { } scope)
        {
            var created = await create.HandleAsync(
                new CreateApiResourceRequest
                {
                    Scope = declared.Scope,
                    Audience = declared.Audience,
                    DisplayName = declared.DisplayName,
                    Description = declared.Description,
                },
                cancellationToken);
            return created.IsSuccess ? ProvisioningChange.Created : created.Error;
        }

        var existing = await scopes.ToResponseAsync(scope, cancellationToken);
        if (existing.IsSystem)
        {
            return ApiResourceErrors.SystemResourceReadOnly;
        }

        if (existing.Audience != (Declared.Text(declared.Audience) ?? existing.Scope))
        {
            return ProvisioningErrors.FixedProperty("audience");
        }

        if (existing.DisplayName == Declared.Text(declared.DisplayName)
            && existing.Description == Declared.Text(declared.Description))
        {
            return ProvisioningChange.Unchanged;
        }

        var updated = await update.HandleAsync(
            existing.Id,
            new UpdateApiResourceRequest { DisplayName = declared.DisplayName, Description = declared.Description },
            cancellationToken);
        return updated.IsSuccess ? ProvisioningChange.Updated : updated.Error;
    }
}
