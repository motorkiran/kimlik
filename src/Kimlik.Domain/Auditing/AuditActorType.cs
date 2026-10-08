namespace Kimlik.Domain.Auditing;

public enum AuditActorType
{
    /// <summary>An unauthenticated caller, such as someone attempting to sign in.</summary>
    Anonymous,

    User,

    Client,

    /// <summary>Kimlik itself, for example a background job.</summary>
    System,
}
