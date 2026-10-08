namespace Kimlik.Application.Abstractions;

public interface IUserSessions
{
    /// <summary>Revokes every authorization and token issued to the user, on every client.</summary>
    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken);
}
