using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OidcPermissions = OpenIddict.Abstractions.OpenIddictConstants.Permissions;

namespace Kimlik.Application.ApiResources;

/// <summary>Registers an API, so that clients can be allowed its scope and request tokens for it.</summary>
public sealed class CreateApiResourceHandler(IKimlikDbContext context, IOpenIddictScopeManager scopes, IAuditLog auditLog)
{
    public async Task<Result<ApiResourceResponse>> HandleAsync(CreateApiResourceRequest request, CancellationToken cancellationToken)
    {
        var name = request.Scope.Trim();
        var audience = request.Audience?.Trim() is { Length: > 0 } given ? given : name;

        if (!ApiResourceNames.IsValidScope(name))
        {
            return ApiResourceErrors.InvalidScope;
        }

        if (ApiResourceNames.IsReservedScope(name))
        {
            return ApiResourceErrors.ReservedScope;
        }

        if (!ApiResourceNames.IsValidAudience(audience))
        {
            return ApiResourceErrors.InvalidAudience;
        }

        if (await scopes.FindByNameAsync(name, cancellationToken) is not null)
        {
            return ApiResourceErrors.AlreadyExists;
        }

        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = name,
            DisplayName = NullIfBlank(request.DisplayName),
            Description = NullIfBlank(request.Description),
            Resources = { audience },
        };

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        object scope;
        try
        {
            scope = await scopes.CreateAsync(descriptor, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return ApiResourceErrors.AlreadyExists;
        }

        var response = await scopes.ToResponseAsync(scope, cancellationToken);
        auditLog.Record(AuditActions.ApiResourceCreated, AuditSubject.ApiResource(response.Id), new Dictionary<string, object?>
        {
            ["scope"] = name,
            ["audience"] = audience,
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return response;
    }

    internal static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Changes how an API resource is presented; its scope and audience are fixed, as clients and APIs depend on them.</summary>
public sealed class UpdateApiResourceHandler(IKimlikDbContext context, IOpenIddictScopeManager scopes, IAuditLog auditLog)
{
    public async Task<Result<ApiResourceResponse>> HandleAsync(Guid id, UpdateApiResourceRequest request, CancellationToken cancellationToken)
    {
        if (await scopes.FindByIdAsync(id.ToString(), cancellationToken) is not { } scope)
        {
            return ApiResourceErrors.NotFound;
        }

        if (!request.HasDisplayName && !request.HasDescription)
        {
            return await scopes.ToResponseAsync(scope, cancellationToken);
        }

        if (await scopes.GetNameAsync(scope, cancellationToken) == KimlikScopes.Api)
        {
            return ApiResourceErrors.SystemResourceReadOnly;
        }

        var descriptor = new OpenIddictScopeDescriptor();
        await scopes.PopulateAsync(descriptor, scope, cancellationToken);

        if (request.HasDisplayName)
        {
            descriptor.DisplayName = CreateApiResourceHandler.NullIfBlank(request.DisplayName);
            descriptor.DisplayNames.Clear();
        }

        if (request.HasDescription)
        {
            descriptor.Description = CreateApiResourceHandler.NullIfBlank(request.Description);
            descriptor.Descriptions.Clear();
        }

        await scopes.UpdateAsync(scope, descriptor, cancellationToken);
        auditLog.Record(AuditActions.ApiResourceUpdated, AuditSubject.ApiResource(id));
        await context.SaveChangesAsync(cancellationToken);

        return await scopes.ToResponseAsync(scope, cancellationToken);
    }
}

/// <summary>
/// Deletes an API resource and takes its scope away from every client. Access tokens already issued for it
/// stay valid until they expire.
/// </summary>
public sealed class DeleteApiResourceHandler(
    IKimlikDbContext context,
    IOpenIddictScopeManager scopes,
    IOpenIddictApplicationManager applications,
    IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await scopes.FindByIdAsync(id.ToString(), cancellationToken) is not { } scope)
        {
            return ApiResourceErrors.NotFound;
        }

        var name = (await scopes.GetNameAsync(scope, cancellationToken))!;
        if (name == KimlikScopes.Api)
        {
            return ApiResourceErrors.SystemResourceReadOnly;
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        var permission = OidcPermissions.Prefixes.Scope + name;
        var holders = await context.Applications
            .Where(application => application.Permissions!.Contains($"\"{permission}\""))
            .ToListAsync(cancellationToken);

        foreach (var application in holders)
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(descriptor, application, cancellationToken);
            descriptor.Permissions.Remove(permission);
            await applications.UpdateAsync(application, descriptor, cancellationToken);
        }

        await scopes.DeleteAsync(scope, cancellationToken);
        auditLog.Record(AuditActions.ApiResourceDeleted, AuditSubject.ApiResource(id), new Dictionary<string, object?> { ["scope"] = name });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success();
    }
}
