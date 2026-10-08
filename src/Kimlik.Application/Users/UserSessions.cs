using Kimlik.Application.Access;
using Kimlik.Application.Accounts;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Common;

namespace Kimlik.Application.Users;

public sealed class ListUserSessionsHandler(ApplicationSessions sessions)
{
    public Task<Result<IReadOnlyList<SessionResponse>>> HandleAsync(Guid userId, CancellationToken cancellationToken) =>
        sessions.ListAsync(userId, cancellationToken);
}

/// <summary>Signs one application out for a user, such as one the user no longer trusts.</summary>
public sealed class RevokeUserSessionHandler(ApplicationSessions sessions, AccessGuard guard)
{
    public async Task<Result> HandleAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var guardResult = await guard.EnsureCanManageUserAsync(userId, cancellationToken);
        return guardResult.IsFailure ? guardResult : await sessions.RevokeAsync(userId, sessionId, cancellationToken);
    }
}

/// <summary>Signs a user out everywhere, such as after their device was stolen.</summary>
public sealed class RevokeUserSessionsHandler(ApplicationSessions sessions, AccessGuard guard)
{
    public async Task<Result> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var guardResult = await guard.EnsureCanManageUserAsync(userId, cancellationToken);
        return guardResult.IsFailure ? guardResult : await sessions.RevokeAllAsync(userId, cancellationToken);
    }
}
