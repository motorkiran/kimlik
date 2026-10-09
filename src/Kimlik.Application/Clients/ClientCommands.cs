using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Kimlik.Application.Clients;

/// <summary>
/// Registers a client from a preset. Web and service clients get a secret, returned only here, unless they authenticate
/// with keys.
/// </summary>
public sealed class CreateClientHandler(
    IKimlikDbContext context,
    IOpenIddictApplicationManager applications,
    AccessGuard guard,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public Task<Result<CreatedClientResponse>> HandleAsync(CreateClientRequest request, CancellationToken cancellationToken) =>
        CreateAsync(request, secret: null, cancellationToken);

    /// <summary>
    /// Creates the client with <paramref name="secret"/>, or with a generated secret when it is <see langword="null"/> and the
    /// client has no keys.
    /// </summary>
    internal async Task<Result<CreatedClientResponse>> CreateAsync(CreateClientRequest request, string? secret, CancellationToken cancellationToken)
    {
        var clientId = request.ClientId.Trim();
        if (!ClientPresets.IsValidClientId(clientId))
        {
            return ClientErrors.InvalidClientId;
        }

        var settings = new ClientSettings(
            request.DisplayName, request.FirstParty, request.RedirectUris, request.PostLogoutRedirectUris, request.Scopes, request.RequireOrganization, request.RequirePushedAuthorization);
        var validation = await ClientPresets.ValidateAsync(context, request.Type, settings, cancellationToken);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        if (ClientPresets.ActsForUsersInKimlik(request.Type, settings.Scopes) && guard.EnsureHoldsAllSystemPermissions() is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var roles = await ResolveRolesAsync(request, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        if (await applications.FindByClientIdAsync(clientId, cancellationToken) is not null)
        {
            return ClientErrors.AlreadyExists;
        }

        var keys = ClientPresets.CheckKeys(request.Type, request.JsonWebKeySet);
        if (keys.IsFailure)
        {
            return keys.Error;
        }

        if (secret is not null)
        {
            var secretCheck = keys.Value is null ? ClientPresets.CheckSecret(request.Type, secret) : ClientErrors.SecretOrKeys;
            if (secretCheck.IsFailure)
            {
                return secretCheck.Error;
            }
        }
        else if (ClientPresets.IsConfidential(request.Type) && keys.Value is null)
        {
            secret = ClientPresets.GenerateSecret();
        }

        var descriptor = new OpenIddictApplicationDescriptor { ClientId = clientId, ClientSecret = secret, JsonWebKeySet = keys.Value };
        ClientPresets.Apply(descriptor, request.Type, settings);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        object application;
        try
        {
            application = await applications.CreateAsync(descriptor, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return ClientErrors.AlreadyExists;
        }

        var id = await applications.GetIdentifierAsync(application, cancellationToken);
        var now = timeProvider.GetUtcNow();
        context.ClientRoles.AddRange(roles.Value.Select(role => new ClientRole(id, role.Id, now)));
        auditLog.Record(AuditActions.ClientCreated, AuditSubject.Client(id), new Dictionary<string, object?>
        {
            ["clientId"] = clientId,
            ["type"] = request.Type.ToString(),
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreatedClientResponse(
            await applications.ToResponseAsync(application, roles.Value.Select(role => role.Key), cancellationToken),
            secret);
    }

    private async Task<Result<List<Role>>> ResolveRolesAsync(CreateClientRequest request, CancellationToken cancellationToken)
    {
        if (request.Roles.Count == 0)
        {
            return new List<Role>();
        }

        if (request.Type != ClientType.Service)
        {
            return ClientErrors.RolesNotSupported;
        }

        var roles = await RoleSet.ResolveGlobalAsync(context, request.Roles, cancellationToken);
        if (roles.IsFailure)
        {
            return roles;
        }

        var guardResult = await guard.EnsureCanGrantRolesAsync([.. roles.Value.Select(role => role.Id)], cancellationToken);
        return guardResult.IsSuccess ? roles : guardResult.Error;
    }
}

/// <summary>
/// Changes the settings of a client; its client ID and type are fixed. New keys replace the secret, or the previous keys.
/// </summary>
public sealed class UpdateClientHandler(IKimlikDbContext context, IOpenIddictApplicationManager applications, AccessGuard guard, IAuditLog auditLog)
{
    public async Task<Result<ClientResponse>> HandleAsync(Guid id, UpdateClientRequest request, CancellationToken cancellationToken)
    {
        if (await applications.FindByIdAsync(id.ToString(), cancellationToken) is not { } application)
        {
            return ClientErrors.NotFound;
        }

        var guardResult = await guard.EnsureCanManageClientAsync(id, cancellationToken);
        if (guardResult.IsFailure)
        {
            return guardResult.Error;
        }

        var current = await applications.ToResponseAsync(application, [], cancellationToken);
        var settings = new ClientSettings(
            request.HasDisplayName ? request.DisplayName ?? string.Empty : current.DisplayName ?? current.ClientId,
            request.FirstParty ?? current.FirstParty,
            request.HasRedirectUris ? request.RedirectUris ?? [] : current.RedirectUris,
            request.HasPostLogoutRedirectUris ? request.PostLogoutRedirectUris ?? [] : current.PostLogoutRedirectUris,
            request.HasScopes ? request.Scopes ?? [] : current.Scopes,
            request.RequireOrganization ?? current.RequireOrganization,
            request.RequirePushedAuthorization ?? current.RequirePushedAuthorization);

        var validation = await ClientPresets.ValidateAsync(context, current.Type, settings, cancellationToken);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        var keys = request.HasJsonWebKeySet && request.JsonWebKeySet is null
            ? ClientErrors.KeysRequired
            : ClientPresets.CheckKeys(current.Type, request.JsonWebKeySet);
        if (keys.IsFailure)
        {
            return keys.Error;
        }

        if ((ClientPresets.ActsForUsersInKimlik(current.Type, current.Scopes) || ClientPresets.ActsForUsersInKimlik(current.Type, settings.Scopes))
            && guard.EnsureHoldsAllSystemPermissions() is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, cancellationToken);
        ClientPresets.Apply(descriptor, current.Type, settings);
        if (keys.Value is not null)
        {
            descriptor.JsonWebKeySet = keys.Value;
            descriptor.ClientSecret = null;
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await applications.UpdateAsync(application, descriptor, cancellationToken);
        auditLog.Record(AuditActions.ClientUpdated, AuditSubject.Client(id));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await applications.ToResponseAsync(context, application, cancellationToken);
    }
}

/// <summary>
/// Replaces the secret of a web or service client; the previous secret, or the keys the client authenticated with, stop
/// working at once. Tokens already issued stay valid until they expire.
/// </summary>
public sealed class RegenerateClientSecretHandler(IKimlikDbContext context, IOpenIddictApplicationManager applications, AccessGuard guard, IAuditLog auditLog)
{
    public Task<Result<ClientSecretResponse>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        ReplaceAsync(id, ClientPresets.GenerateSecret(), cancellationToken);

    internal async Task<Result<ClientSecretResponse>> ReplaceAsync(Guid id, string secret, CancellationToken cancellationToken)
    {
        if (await applications.FindByIdAsync(id.ToString(), cancellationToken) is not { } application)
        {
            return ClientErrors.NotFound;
        }

        var secretCheck = ClientPresets.CheckSecret(await applications.GetPresetAsync(application, cancellationToken), secret);
        if (secretCheck.IsFailure)
        {
            return secretCheck.Error;
        }

        var guardResult = await guard.EnsureCanManageClientAsync(id, cancellationToken);
        if (guardResult.IsFailure)
        {
            return guardResult.Error;
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, cancellationToken);
        descriptor.ClientSecret = secret;
        descriptor.JsonWebKeySet = null;
        await applications.UpdateAsync(application, descriptor, cancellationToken);
        auditLog.Record(AuditActions.ClientSecretRegenerated, AuditSubject.Client(id));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ClientSecretResponse(secret);
    }
}

/// <summary>Replaces the global roles of a service client. Granting system permissions requires holding them.</summary>
public sealed class SetClientRolesHandler(
    IKimlikDbContext context,
    IOpenIddictApplicationManager applications,
    AccessGuard guard,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<ClientResponse>> HandleAsync(Guid id, SetRolesRequest request, CancellationToken cancellationToken)
    {
        if (await applications.FindByIdAsync(id.ToString(), cancellationToken) is not { } application)
        {
            return ClientErrors.NotFound;
        }

        if (await applications.GetPresetAsync(application, cancellationToken) != ClientType.Service)
        {
            return ClientErrors.RolesNotSupported;
        }

        var targetGuard = await guard.EnsureCanManageClientAsync(id, cancellationToken);
        if (targetGuard.IsFailure)
        {
            return targetGuard.Error;
        }

        var resolution = await RoleSet.ResolveGlobalAsync(context, request.Roles, cancellationToken);
        if (resolution.IsFailure)
        {
            return resolution.Error;
        }

        var wanted = resolution.Value;
        var current = await context.ClientRoles.Where(assignment => assignment.ApplicationId == id).ToListAsync(cancellationToken);
        var added = wanted.Where(role => current.TrueForAll(assignment => assignment.RoleId != role.Id)).ToList();
        var removed = current.Where(assignment => !wanted.Exists(role => role.Id == assignment.RoleId)).ToList();

        var grantGuard = await guard.EnsureCanGrantRolesAsync([.. added.Select(role => role.Id)], cancellationToken);
        if (grantGuard.IsFailure)
        {
            return grantGuard.Error;
        }

        if (added.Count > 0 || removed.Count > 0)
        {
            var now = timeProvider.GetUtcNow();
            context.ClientRoles.AddRange(added.Select(role => new ClientRole(id, role.Id, now)));
            context.ClientRoles.RemoveRange(removed);
            auditLog.Record(AuditActions.ClientRolesChanged, AuditSubject.Client(id), new Dictionary<string, object?>
            {
                ["roles"] = wanted.Select(role => role.Key).Order(StringComparer.Ordinal).ToArray(),
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        return await applications.ToResponseAsync(application, wanted.Select(role => role.Key), cancellationToken);
    }
}

/// <summary>Deletes a client with its authorizations, tokens and role assignments.</summary>
public sealed class DeleteClientHandler(IKimlikDbContext context, IOpenIddictApplicationManager applications, AccessGuard guard, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await applications.FindByIdAsync(id.ToString(), cancellationToken) is not { } application)
        {
            return ClientErrors.NotFound;
        }

        var guardResult = await guard.EnsureCanManageClientAsync(id, cancellationToken);
        if (guardResult.IsFailure)
        {
            return guardResult;
        }

        var clientId = await applications.GetClientIdAsync(application, cancellationToken);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await applications.DeleteAsync(application, cancellationToken);
        auditLog.Record(AuditActions.ClientDeleted, AuditSubject.Client(id), new Dictionary<string, object?> { ["clientId"] = clientId });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success();
    }
}
