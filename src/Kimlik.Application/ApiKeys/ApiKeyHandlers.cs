using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Plans;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using DomainUserStatus = Kimlik.Domain.Users.UserStatus;

namespace Kimlik.Application.ApiKeys;

/// <summary>Lists API keys in creation order, optionally those of one user or one organization.</summary>
public sealed record ListApiKeysQuery(Guid? UserId, Guid? OrganizationId, string? Cursor, int? Limit);

public sealed class ListApiKeysHandler(IKimlikDbContext context, ApiKeyStore store)
{
    public Task<Result<Page<ApiKeyResponse>>> HandleAsync(ListApiKeysQuery query, CancellationToken cancellationToken)
    {
        var keys = context.ApiKeys.AsQueryable();
        if (query.UserId is { } userId)
        {
            keys = keys.Where(key => key.UserId == userId);
        }

        if (query.OrganizationId is { } organizationId)
        {
            keys = keys.Where(key => key.OrganizationId == organizationId);
        }

        return store.ListAsync(keys, query.Cursor, query.Limit, cancellationToken);
    }
}

public sealed class RevokeApiKeyHandler(IKimlikDbContext context, ApiKeyStore store)
{
    public Task<Result> HandleAsync(Guid keyId, CancellationToken cancellationToken) =>
        store.RevokeAsync(context.ApiKeys.Where(key => key.Id == keyId), cancellationToken);
}

/// <summary>
/// Tells a resource server whether an API key works and what it may do. A user's key only keeps the permissions the
/// user still holds through their global roles, and stops working while the user is suspended.
/// </summary>
public sealed class VerifyApiKeyHandler(
    IKimlikDbContext context, AccessResolver access, Entitlements entitlements, ApiKeyStore store, TimeProvider timeProvider)
{
    /// <summary>How often the time a key was last used is written, at most.</summary>
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    private static readonly ApiKeyVerificationResponse Inactive = new(Active: false);

    public async Task<ApiKeyVerificationResponse> HandleAsync(VerifyApiKeyRequest request, CancellationToken cancellationToken)
    {
        if (!ApiKeyFormat.IsWellFormed(request.Key))
        {
            return Inactive;
        }

        var hash = ApiKeySecrets.Hash(request.Key);
        var now = timeProvider.GetUtcNow();
        if (await context.ApiKeys.AsNoTracking().SingleOrDefaultAsync(key => key.SecretHash == hash, cancellationToken) is not { } key
            || !key.IsActiveAt(now))
        {
            return Inactive;
        }

        IEnumerable<string> permissions = (await store.PermissionsOfAsync([key.Id], cancellationToken))[key.Id];
        EntitlementSource source;
        if (key.UserId is { } userId)
        {
            if (!await context.Users.AnyAsync(user => user.Id == userId && user.Status == DomainUserStatus.Active, cancellationToken))
            {
                return Inactive;
            }

            var held = await access.ForUserAsync(userId, organizationId: null, cancellationToken);
            permissions = permissions.Intersect(held.Permissions, StringComparer.Ordinal);
            source = await entitlements.SourceOfAsync(Subscriber.User(userId), cancellationToken);
        }
        else
        {
            source = await entitlements.SourceOfAsync(Subscriber.Organization(key.OrganizationId!.Value), cancellationToken);
        }

        if (key.LastUsedAt is not { } lastUsed || now - lastUsed >= LastUsedPrecision)
        {
            await context.ApiKeys.Where(candidate => candidate.Id == key.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.LastUsedAt, now), cancellationToken);
        }

        return new ApiKeyVerificationResponse(true, key.Id, key.UserId, key.OrganizationId, [.. permissions], source.Plan?.Key, key.ExpiresAt, source.IsCustom);
    }
}
