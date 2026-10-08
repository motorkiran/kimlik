using Kimlik.Application.Abstractions;
using Kimlik.Application.Roles;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Provisioning;

public sealed class RoleProvisioner(
    IKimlikDbContext context,
    CreateRoleHandler create,
    UpdateRoleHandler update,
    SetRolePermissionsHandler setPermissions)
{
    public async Task<Result<ProvisioningChange>> ApplyAsync(ProvisionedRole declared, CancellationToken cancellationToken)
    {
        var existing = await context.Roles.AsNoTracking().SingleOrDefaultAsync(role => role.Key == declared.Key, cancellationToken);
        if (existing is null)
        {
            var created = await create.HandleAsync(
                new CreateRoleRequest
                {
                    Key = declared.Key,
                    Name = declared.Name,
                    Description = declared.Description,
                    Scope = declared.Scope,
                    Permissions = declared.Permissions ?? [],
                },
                cancellationToken);
            return created.IsSuccess ? ProvisioningChange.Created : created.Error;
        }

        if (existing.IsSystem)
        {
            return AccessErrors.SystemDefinitionReadOnly;
        }

        if (existing.Scope != declared.Scope.ToDomain())
        {
            return ProvisioningErrors.FixedProperty("scope");
        }

        var change = ProvisioningChange.Unchanged;

        if (existing.Name != declared.Name.Trim() || existing.Description != Declared.Text(declared.Description))
        {
            var updated = await update.HandleAsync(existing.Id, new UpdateRoleRequest { Name = declared.Name, Description = declared.Description }, cancellationToken);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            change = ProvisioningChange.Updated;
        }

        if (declared.Permissions is not null
            && !Declared.SameSet((await context.PermissionsOfAsync([existing.Id], cancellationToken))[existing.Id], declared.Permissions))
        {
            var replaced = await setPermissions.HandleAsync(existing.Id, new SetPermissionsRequest { Permissions = declared.Permissions }, cancellationToken);
            if (replaced.IsFailure)
            {
                return replaced.Error;
            }

            change = ProvisioningChange.Updated;
        }

        return change;
    }
}
