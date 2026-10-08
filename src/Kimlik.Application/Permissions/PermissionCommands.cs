using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Permissions;

/// <summary>Defines a permission of the applications that use Kimlik.</summary>
public sealed class CreatePermissionHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<PermissionResponse>> HandleAsync(CreatePermissionRequest request, CancellationToken cancellationToken)
    {
        var created = Permission.Create(request.Key, request.Description, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        var permission = created.Value;
        if (await context.Permissions.AnyAsync(existing => existing.Key == permission.Key, cancellationToken))
        {
            return PermissionErrors.AlreadyExists;
        }

        context.Permissions.Add(permission);
        auditLog.Record(AuditActions.PermissionCreated, AuditSubject.Permission(permission.Id), new Dictionary<string, object?> { ["key"] = permission.Key });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return PermissionErrors.AlreadyExists;
        }

        return permission.ToResponse();
    }
}

/// <summary>Changes the description of a permission; its key is fixed, as tokens and code depend on it.</summary>
public sealed class UpdatePermissionHandler(IKimlikDbContext context, IAuditLog auditLog)
{
    public async Task<Result<PermissionResponse>> HandleAsync(Guid permissionId, UpdatePermissionRequest request, CancellationToken cancellationToken)
    {
        if (await context.Permissions.SingleOrDefaultAsync(permission => permission.Id == permissionId, cancellationToken) is not { } permission)
        {
            return PermissionErrors.NotFound;
        }

        if (request.HasDescription)
        {
            var described = permission.Describe(request.Description);
            if (described.IsFailure)
            {
                return described.Error;
            }

            auditLog.Record(AuditActions.PermissionUpdated, AuditSubject.Permission(permission.Id));
            await context.SaveChangesAsync(cancellationToken);
        }

        return permission.ToResponse();
    }
}

/// <summary>Deletes a permission and removes it from every role; tokens issued from then on no longer carry it.</summary>
public sealed class DeletePermissionHandler(IKimlikDbContext context, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid permissionId, CancellationToken cancellationToken)
    {
        if (await context.Permissions.SingleOrDefaultAsync(permission => permission.Id == permissionId, cancellationToken) is not { } permission)
        {
            return PermissionErrors.NotFound;
        }

        if (permission.IsSystem)
        {
            return AccessErrors.SystemDefinitionReadOnly;
        }

        context.Permissions.Remove(permission);
        auditLog.Record(AuditActions.PermissionDeleted, AuditSubject.Permission(permission.Id), new Dictionary<string, object?> { ["key"] = permission.Key });
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
