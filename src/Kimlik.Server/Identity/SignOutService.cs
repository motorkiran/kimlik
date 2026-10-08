using System.Security.Claims;
using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Server.Identity;

/// <summary>Ends the sign-in session of the hosted pages, from the sign-out page or an RP-initiated logout.</summary>
public sealed class SignOutService(SignInManager<User> signInManager, IAuditLog auditLog, IKimlikDbContext context)
{
    public async Task SignOutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        await signInManager.SignOutAsync();

        if (Guid.TryParse(signInManager.UserManager.GetUserId(principal), out var userId))
        {
            auditLog.Record(AuditActions.UserSignedOut, AuditSubject.User(userId), actor: AuditActor.User(userId));
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
