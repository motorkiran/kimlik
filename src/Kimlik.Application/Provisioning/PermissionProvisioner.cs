using Kimlik.Application.Abstractions;
using Kimlik.Application.Permissions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Provisioning;

public sealed class PermissionProvisioner(IKimlikDbContext context, CreatePermissionHandler create, UpdatePermissionHandler update)
{
    public async Task<Result<ProvisioningChange>> ApplyAsync(ProvisionedPermission declared, CancellationToken cancellationToken)
    {
        var existing = await context.Permissions.AsNoTracking().SingleOrDefaultAsync(permission => permission.Key == declared.Key, cancellationToken);
        if (existing is null)
        {
            var created = await create.HandleAsync(new CreatePermissionRequest { Key = declared.Key, Description = declared.Description }, cancellationToken);
            return created.IsSuccess ? ProvisioningChange.Created : created.Error;
        }

        if (existing.IsSystem)
        {
            return AccessErrors.SystemDefinitionReadOnly;
        }

        if (existing.Description == Declared.Text(declared.Description))
        {
            return ProvisioningChange.Unchanged;
        }

        var updated = await update.HandleAsync(existing.Id, new UpdatePermissionRequest { Description = declared.Description }, cancellationToken);
        return updated.IsSuccess ? ProvisioningChange.Updated : updated.Error;
    }
}
