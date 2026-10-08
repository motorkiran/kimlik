using Kimlik.Application.Abstractions;
using Kimlik.Application.Users;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Application.Accounts;

/// <summary>
/// A user's sessions: the applications that can get tokens for them. The user manages their own, and administrators
/// those of any user.
/// </summary>
public sealed class ApplicationSessions(
    UserManager<User> userManager,
    IUserSessions sessions,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictApplicationManager applications,
    IOpenIddictTokenManager tokens,
    IKimlikDbContext context,
    IAuditLog auditLog)
{
    public async Task<Result<IReadOnlyList<SessionResponse>>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is null)
        {
            return UserErrors.NotFound;
        }

        var result = new List<SessionResponse>();
        await foreach (var authorization in authorizations.FindBySubjectAsync(userId.ToString(), cancellationToken))
        {
            if (!await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken))
            {
                continue;
            }

            var application = await authorizations.GetApplicationIdAsync(authorization, cancellationToken) is { } applicationId
                ? await applications.FindByIdAsync(applicationId, cancellationToken)
                : null;

            result.Add(new SessionResponse(
                Guid.Parse((await authorizations.GetIdAsync(authorization, cancellationToken))!),
                application is null ? null : await applications.GetClientIdAsync(application, cancellationToken),
                application is null ? null : await applications.GetDisplayNameAsync(application, cancellationToken),
                [.. await authorizations.GetScopesAsync(authorization, cancellationToken)],
                await authorizations.GetCreationDateAsync(authorization, cancellationToken)));
        }

        return result.OrderByDescending(session => session.CreatedAt).ToList();
    }

    /// <summary>Signs an application out: its tokens stop working and it has to ask the user again.</summary>
    public async Task<Result> RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (await authorizations.FindByIdAsync(sessionId.ToString(), cancellationToken) is not { } authorization
            || await authorizations.GetSubjectAsync(authorization, cancellationToken) != userId.ToString())
        {
            return AccountErrors.SessionNotFound;
        }

        await authorizations.TryRevokeAsync(authorization, cancellationToken);
        await tokens.RevokeByAuthorizationIdAsync(sessionId.ToString(), cancellationToken);
        auditLog.Record(AuditActions.UserSessionRevoked, AuditSubject.User(userId), new Dictionary<string, object?> { ["session"] = sessionId });
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Signs out everywhere: every application session and token ends, and browser sessions at their next check.</summary>
    public async Task<Result> RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        await userManager.UpdateSecurityStampAsync(user);
        await sessions.RevokeAllAsync(userId, cancellationToken);
        auditLog.Record(AuditActions.UserSessionRevoked, AuditSubject.User(userId), new Dictionary<string, object?> { ["session"] = "all" });
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
