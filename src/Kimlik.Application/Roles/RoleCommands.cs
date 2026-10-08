using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Roles;

/// <summary>Defines a role. Including system permissions requires the caller to hold them.</summary>
public sealed class CreateRoleHandler(IKimlikDbContext context, AccessGuard guard, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<RoleResponse>> HandleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var created = Role.Create(request.Key, request.Name, request.Description, request.Scope.ToDomain(), now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var resolution = await PermissionSet.ResolveAsync(context, request.Permissions, cancellationToken);
        if (resolution.IsFailure)
        {
            return resolution.Error;
        }

        var permissions = resolution.Value;
        var guardResult = guard.EnsureCanGrant(permissions.Select(permission => permission.Key));
        if (guardResult.IsFailure)
        {
            return guardResult.Error;
        }

        var role = created.Value;
        if (await context.Roles.AnyAsync(existing => existing.Key == role.Key, cancellationToken))
        {
            return RoleErrors.AlreadyExists;
        }

        var assigned = role.SetPermissions(permissions, now);
        if (assigned.IsFailure)
        {
            return assigned.Error;
        }

        context.Roles.Add(role);
        auditLog.Record(AuditActions.RoleCreated, AuditSubject.Role(role.Id), new Dictionary<string, object?>
        {
            ["key"] = role.Key,
            ["permissions"] = permissions.SortedKeys(),
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return RoleErrors.AlreadyExists;
        }

        return role.ToResponse(permissions.Select(permission => permission.Key));
    }
}

/// <summary>Renames or describes a role; its key is fixed, as tokens and code depend on it.</summary>
public sealed class UpdateRoleHandler(IKimlikDbContext context, AccessGuard guard, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<RoleResponse>> HandleAsync(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        if (await context.Roles.SingleOrDefaultAsync(role => role.Id == roleId, cancellationToken) is not { } role)
        {
            return RoleErrors.NotFound;
        }

        if (!request.HasName && !request.HasDescription)
        {
            return await context.ToResponseAsync(role, cancellationToken);
        }

        var guardResult = await guard.EnsureCanManageRoleAsync(role.Id, cancellationToken);
        if (guardResult.IsFailure)
        {
            return guardResult.Error;
        }

        var updated = role.Update(
            request.HasName ? request.Name! : role.Name,
            request.HasDescription ? request.Description : role.Description,
            timeProvider.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        auditLog.Record(AuditActions.RoleUpdated, AuditSubject.Role(role.Id));
        await context.SaveChangesAsync(cancellationToken);

        return await context.ToResponseAsync(role, cancellationToken);
    }
}

/// <summary>
/// Replaces the permissions of a role. The caller must hold every system permission the role has before and
/// after the change. Tokens issued from then on carry the new permissions.
/// </summary>
public sealed class SetRolePermissionsHandler(IKimlikDbContext context, AccessGuard guard, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<RoleResponse>> HandleAsync(Guid roleId, SetPermissionsRequest request, CancellationToken cancellationToken)
    {
        if (await context.Roles.Include(role => role.Permissions).SingleOrDefaultAsync(role => role.Id == roleId, cancellationToken) is not { } role)
        {
            return RoleErrors.NotFound;
        }

        var resolution = await PermissionSet.ResolveAsync(context, request.Permissions, cancellationToken);
        if (resolution.IsFailure)
        {
            return resolution.Error;
        }

        var permissions = resolution.Value;
        var currentKeys = (await context.PermissionsOfAsync([role.Id], cancellationToken))[role.Id];
        var guardResult = guard.EnsureCanGrant(currentKeys.Concat(permissions.Select(permission => permission.Key)));
        if (guardResult.IsFailure)
        {
            return guardResult.Error;
        }

        var changed = role.SetPermissions(permissions, timeProvider.GetUtcNow());
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        auditLog.Record(AuditActions.RolePermissionsChanged, AuditSubject.Role(role.Id), new Dictionary<string, object?>
        {
            ["permissions"] = permissions.SortedKeys(),
        });
        await context.SaveChangesAsync(cancellationToken);

        return role.ToResponse(permissions.Select(permission => permission.Key));
    }
}

/// <summary>Deletes a role and its assignments; tokens issued from then on no longer carry it.</summary>
public sealed class DeleteRoleHandler(IKimlikDbContext context, AccessGuard guard, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        if (await context.Roles.SingleOrDefaultAsync(role => role.Id == roleId, cancellationToken) is not { } role)
        {
            return RoleErrors.NotFound;
        }

        if (role.IsSystem)
        {
            return AccessErrors.SystemDefinitionReadOnly;
        }

        var guardResult = await guard.EnsureCanManageRoleAsync(role.Id, cancellationToken);
        if (guardResult.IsFailure)
        {
            return guardResult;
        }

        context.Roles.Remove(role);
        auditLog.Record(AuditActions.RoleDeleted, AuditSubject.Role(role.Id), new Dictionary<string, object?> { ["key"] = role.Key });
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
