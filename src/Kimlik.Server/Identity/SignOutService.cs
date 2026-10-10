using System.Security.Claims;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Server.Identity;

/// <summary>
/// Ends the sign-in session of the hosted pages, from the sign-out page or an RP-initiated logout, and tells the web apps
/// the session signed in to (OpenID Connect Back-Channel Logout).
/// </summary>
public sealed class SignOutService(SignInManager<User> signInManager, BackChannelLogout backChannelLogout, IAuditLog auditLog, IKimlikDbContext context)
{
    public async Task SignOutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        await signInManager.SignOutAsync();

        if (Guid.TryParse(signInManager.UserManager.GetUserId(principal), out var userId))
        {
            if (SignInFlow.SessionIdOf(principal) is { } sessionId)
            {
                await backChannelLogout.EndSessionAsync(userId, sessionId, cancellationToken);
            }

            auditLog.Record(AuditActions.UserSignedOut, AuditSubject.User(userId), actor: AuditActor.User(userId));
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
