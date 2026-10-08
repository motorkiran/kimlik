namespace Kimlik.Domain.Auditing;

/// <summary>Who performed an audited action.</summary>
public sealed record AuditActor(AuditActorType Type, string? Id)
{
    public static readonly AuditActor Anonymous = new(AuditActorType.Anonymous, null);

    public static readonly AuditActor System = new(AuditActorType.System, null);

    public static AuditActor User(Guid userId) => new(AuditActorType.User, userId.ToString());

    public static AuditActor Client(string clientId) => new(AuditActorType.Client, clientId);
}

/// <summary>What an audited action was performed on.</summary>
public sealed record AuditSubject(string Type, string Id)
{
    public static AuditSubject User(Guid userId) => new("user", userId.ToString());

    public static AuditSubject Permission(Guid permissionId) => new("permission", permissionId.ToString());

    public static AuditSubject Role(Guid roleId) => new("role", roleId.ToString());

    public static AuditSubject ApiResource(Guid scopeId) => new("api_resource", scopeId.ToString());

    public static AuditSubject Client(Guid applicationId) => new("client", applicationId.ToString());
}
