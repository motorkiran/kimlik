namespace Kimlik.Domain.Users;

/// <summary>
/// A client with a back-channel logout URI that a browser session of the hosted pages signed the user in to, so that
/// it hears when the session ends.
/// </summary>
public sealed class SessionClient
{
    public const int SessionIdMaxLength = 64;
    public const int ClientIdMaxLength = 100;

    private SessionClient()
    {
    }

    public SessionClient(string sessionId, Guid userId, string clientId, DateTimeOffset signedInAt)
    {
        SessionId = sessionId;
        UserId = userId;
        ClientId = clientId;
        SignedInAt = signedInAt;
    }

    public string SessionId { get; private set; } = null!;

    public Guid UserId { get; private set; }

    public string ClientId { get; private set; } = null!;

    /// <summary>When the session last signed the user in to the client.</summary>
    public DateTimeOffset SignedInAt { get; private set; }

    public void SignInAgain(DateTimeOffset now) => SignedInAt = now;
}
