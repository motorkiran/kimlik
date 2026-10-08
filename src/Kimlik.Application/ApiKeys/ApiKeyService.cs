using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Common;
using Kimlik.Application.Organizations;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.ApiKeys;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.ApiKeys;

/// <summary>
/// API keys as their owners manage them: users their own, and organization members those of their organization, as
/// their <c>kimlik.org.api_keys:*</c> permissions allow. Nobody gives a key a permission they do not hold themselves.
/// </summary>
public sealed class MyApiKeys(IKimlikDbContext context, AccessResolver access, OrganizationGuard guard, ApiKeyStore store)
{
    public Task<Result<Page<ApiKeyResponse>>> ListAsync(Guid userId, string? cursor, int? limit, CancellationToken cancellationToken) =>
        store.ListAsync(context.ApiKeys.Where(key => key.UserId == userId), cursor, limit, cancellationToken);

    /// <summary>A key for the user, with permissions they hold through their global roles.</summary>
    public async Task<Result<CreatedApiKeyResponse>> CreateAsync(Guid userId, CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var held = await access.ForUserAsync(userId, organizationId: null, cancellationToken);
        return await store.CreateAsync(ApiKeyOwner.User(userId), userId, held.Permissions, request, cancellationToken);
    }

    public Task<Result> RevokeAsync(Guid userId, Guid keyId, CancellationToken cancellationToken) =>
        store.RevokeAsync(context.ApiKeys.Where(key => key.Id == keyId && key.UserId == userId), cancellationToken);

    public async Task<Result<Page<ApiKeyResponse>>> ListForOrganizationAsync(
        Guid callerId, Guid organizationId, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, organizationId, SystemPermissions.OrganizationApiKeysRead, cancellationToken);
        return caller.IsFailure
            ? caller.Error
            : await store.ListAsync(context.ApiKeys.Where(key => key.OrganizationId == organizationId), cursor, limit, cancellationToken);
    }

    /// <summary>
    /// A key for the organization, with permissions the caller holds through their roles in it. Their global roles
    /// do not count: the key outlives them, and would otherwise keep a permission its creator lost.
    /// </summary>
    public async Task<Result<CreatedApiKeyResponse>> CreateForOrganizationAsync(
        Guid callerId, Guid organizationId, CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, organizationId, SystemPermissions.OrganizationApiKeysWrite, cancellationToken);
        return caller.IsFailure
            ? caller.Error
            : await store.CreateAsync(ApiKeyOwner.Organization(organizationId), callerId, caller.Value, request, cancellationToken);
    }

    public async Task<Result> RevokeForOrganizationAsync(Guid callerId, Guid organizationId, Guid keyId, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, organizationId, SystemPermissions.OrganizationApiKeysWrite, cancellationToken);
        return caller.IsFailure
            ? caller
            : await store.RevokeAsync(context.ApiKeys.Where(key => key.Id == keyId && key.OrganizationId == organizationId), cancellationToken);
    }
}

/// <summary>Creates, lists and revokes API keys, for whoever is allowed to.</summary>
public sealed class ApiKeyStore(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    /// <summary>How many keys an owner can have that are not revoked.</summary>
    public const int MaxKeysPerOwner = 100;

    public async Task<Result<Page<ApiKeyResponse>>> ListAsync(IQueryable<ApiKey> keys, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        if (after is { } afterId)
        {
            keys = keys.Where(key => key.Id > afterId);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(keys.AsNoTracking().OrderBy(key => key.Id), limit, key => key.Id, cancellationToken);
        var permissions = await PermissionsOfAsync([.. page.Select(key => key.Id)], cancellationToken);
        return new Page<ApiKeyResponse>([.. page.Select(key => key.ToResponse(permissions[key.Id]))], nextCursor);
    }

    internal async Task<Result<CreatedApiKeyResponse>> CreateAsync(
        ApiKeyOwner owner, Guid creatorId, IReadOnlyCollection<string> creatorPermissions, CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var keys = request.Permissions.Distinct(StringComparer.Ordinal).ToList();
        var permissions = await context.Permissions.Where(permission => keys.Contains(permission.Key)).ToListAsync(cancellationToken);
        if (permissions.Count != keys.Count)
        {
            return AccessErrors.UnknownPermission;
        }

        if (permissions.Any(permission => permission.IsSystem))
        {
            return ApiKeyErrors.SystemPermission;
        }

        if (!keys.All(creatorPermissions.Contains))
        {
            return ApiKeyErrors.PermissionNotHeld;
        }

        var ownersKeys = context.ApiKeys.Where(key => key.RevokedAt == null
            && (owner.UserId != null ? key.UserId == owner.UserId : key.OrganizationId == owner.OrganizationId));
        if (await ownersKeys.CountAsync(cancellationToken) >= MaxKeysPerOwner)
        {
            return ApiKeyErrors.LimitReached;
        }

        var (secret, prefix, hash) = ApiKeySecrets.Generate();
        var created = ApiKey.Create(owner, request.Name, permissions, request.ExpiresAt, prefix, hash, creatorId, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        var key = created.Value;
        context.ApiKeys.Add(key);
        auditLog.Record(
            AuditActions.ApiKeyCreated,
            AuditSubject.ApiKey(key.Id),
            new Dictionary<string, object?> { ["name"] = key.Name, ["prefix"] = key.Prefix, ["permissions"] = keys, ["user"] = key.UserId },
            organizationId: key.OrganizationId);
        await context.SaveChangesAsync(cancellationToken);

        return new CreatedApiKeyResponse(key.ToResponse(keys.Order(StringComparer.Ordinal)), secret);
    }

    /// <summary>Revokes the key the query finds, if any; revoking it again changes nothing.</summary>
    internal async Task<Result> RevokeAsync(IQueryable<ApiKey> query, CancellationToken cancellationToken)
    {
        if (await query.SingleOrDefaultAsync(cancellationToken) is not { } key)
        {
            return ApiKeyErrors.NotFound;
        }

        if (key.RevokedAt is null)
        {
            key.Revoke(timeProvider.GetUtcNow());
            auditLog.Record(
                AuditActions.ApiKeyRevoked,
                AuditSubject.ApiKey(key.Id),
                new Dictionary<string, object?> { ["name"] = key.Name, ["prefix"] = key.Prefix, ["user"] = key.UserId },
                organizationId: key.OrganizationId);
            await context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    internal async Task<ILookup<Guid, string>> PermissionsOfAsync(IReadOnlyCollection<Guid> keyIds, CancellationToken cancellationToken) =>
        (await context.ApiKeyPermissions
            .Where(link => keyIds.Contains(link.ApiKeyId))
            .Join(context.Permissions, link => link.PermissionId, permission => permission.Id, (link, permission) => new { link.ApiKeyId, permission.Key })
            .ToListAsync(cancellationToken))
        .OrderBy(link => link.Key, StringComparer.Ordinal)
        .ToLookup(link => link.ApiKeyId, link => link.Key);
}

internal static class ApiKeyMappings
{
    public static ApiKeyResponse ToResponse(this ApiKey key, IEnumerable<string> permissions) =>
        new(key.Id, key.Name, key.Prefix, key.UserId, key.OrganizationId, [.. permissions], key.CreatedBy, key.CreatedAt, key.ExpiresAt, key.LastUsedAt, key.RevokedAt);
}
