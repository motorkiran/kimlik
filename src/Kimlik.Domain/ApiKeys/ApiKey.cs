using Kimlik.Domain.Access;
using Kimlik.Domain.Common;

namespace Kimlik.Domain.ApiKeys;

/// <summary>Who an API key belongs to: a user or an organization.</summary>
public readonly record struct ApiKeyOwner(Guid? UserId, Guid? OrganizationId)
{
    public static ApiKeyOwner User(Guid userId) => new(userId, null);

    public static ApiKeyOwner Organization(Guid organizationId) => new(null, organizationId);
}

/// <summary>
/// A long-lived secret that calls the application's API on behalf of a user or an organization, with a set of the
/// application's permissions. Only a hash of the secret is kept; the secret is shown once, when the key is created.
/// </summary>
public sealed class ApiKey
{
    public const int NameMaxLength = 100;

    private readonly List<ApiKeyPermission> _permissions = [];

    // Used by EF Core.
    private ApiKey()
    {
    }

    public Guid Id { get; private init; }

    public Guid? UserId { get; private init; }

    public Guid? OrganizationId { get; private init; }

    public string Name { get; private init; } = string.Empty;

    /// <summary>The start of the secret, which identifies the key in lists and logs without revealing it.</summary>
    public string Prefix { get; private init; } = string.Empty;

    public string SecretHash { get; private init; } = string.Empty;

    /// <summary>The user who created the key; for an organization's key, the member who did.</summary>
    public Guid? CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? ExpiresAt { get; private init; }

    public DateTimeOffset? LastUsedAt { get; private init; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public IReadOnlyCollection<ApiKeyPermission> Permissions => _permissions;

    public ApiKeyOwner Owner => new(UserId, OrganizationId);

    /// <summary>A new key with permissions of the application; Kimlik's own system permissions cannot be given to keys.</summary>
    public static Result<ApiKey> Create(
        ApiKeyOwner owner,
        string name,
        IReadOnlyCollection<Permission> permissions,
        DateTimeOffset? expiresAt,
        string prefix,
        string secretHash,
        Guid? createdBy,
        DateTimeOffset now)
    {
        if (owner.UserId is null == owner.OrganizationId is null)
        {
            throw new ArgumentException("An API key belongs to either a user or an organization.", nameof(owner));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            return ApiKeyErrors.InvalidName;
        }

        if (expiresAt <= now)
        {
            return ApiKeyErrors.ExpiryInThePast;
        }

        if (permissions.Any(permission => permission.IsSystem))
        {
            return ApiKeyErrors.SystemPermission;
        }

        var key = new ApiKey
        {
            Id = Guid.CreateVersion7(now),
            UserId = owner.UserId,
            OrganizationId = owner.OrganizationId,
            Name = name.Trim(),
            Prefix = prefix,
            SecretHash = secretHash,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

        key._permissions.AddRange(permissions.Select(permission => new ApiKeyPermission(key.Id, permission.Id)));
        return key;
    }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is not { } expiry || now < expiry);

    /// <summary>Stops the key from working. Revoking a revoked key changes nothing.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

/// <summary>A permission given to an API key.</summary>
public sealed class ApiKeyPermission(Guid apiKeyId, Guid permissionId)
{
    public Guid ApiKeyId { get; private init; } = apiKeyId;

    public Guid PermissionId { get; private init; } = permissionId;
}
