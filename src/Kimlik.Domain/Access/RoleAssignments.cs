namespace Kimlik.Domain.Access;

/// <summary>Links a role to one of its permissions.</summary>
public sealed class RolePermission(Guid roleId, Guid permissionId)
{
    public Guid RoleId { get; private init; } = roleId;

    public Guid PermissionId { get; private init; } = permissionId;
}

/// <summary>A global role held by a user.</summary>
public sealed class UserRole(Guid userId, Guid roleId, DateTimeOffset assignedAt)
{
    public Guid UserId { get; private init; } = userId;

    public Guid RoleId { get; private init; } = roleId;

    public DateTimeOffset AssignedAt { get; private init; } = assignedAt;
}

/// <summary>A global role held by a service client, identified by its OpenID Connect application ID.</summary>
public sealed class ClientRole(Guid applicationId, Guid roleId, DateTimeOffset assignedAt)
{
    public Guid ApplicationId { get; private init; } = applicationId;

    public Guid RoleId { get; private init; } = roleId;

    public DateTimeOffset AssignedAt { get; private init; } = assignedAt;
}
