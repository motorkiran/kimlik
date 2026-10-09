using System.Security.Claims;
using Kimlik.Application.Access;
using Kimlik.Application.Mfa;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Admin.Security;

/// <summary>Who may use the admin panel.</summary>
public static class AdminAccess
{
    /// <summary>
    /// The policy of every admin panel page: a sign-in session of a user who holds a system permission for the whole
    /// installation, verified with a second factor when the MFA policy requires one for them (by default, it does for
    /// administrators).
    /// </summary>
    public const string Policy = "KimlikAdmin";

    /// <summary>The reason a session is refused because it still needs a second factor.</summary>
    public const string SecondFactorNeeded = "kimlik.admin.second_factor_needed";

    /// <summary>The reason a session is refused because an administrator acts as its user in it.</summary>
    public const string Impersonating = "kimlik.admin.impersonating";

    /// <summary>The claim and value the sign-in session carries after a second factor (see the server's sign-in flow).</summary>
    internal const string MethodClaim = "amr";
    internal const string MultiFactorMethod = "mfa";

    /// <summary>The claim naming the administrator who acts as the session's user (see the server's sign-in flow).</summary>
    internal const string ActorClaim = "kimlik:actor";

    /// <summary>The permissions of the user for the whole installation, or none for a session without a user.</summary>
    internal static async Task<IReadOnlySet<string>> PermissionsOfAsync(IServiceScopeFactory scopeFactory, ClaimsPrincipal user)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return new HashSet<string>();
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var grant = await scope.ServiceProvider.GetRequiredService<AccessResolver>().ForUserAsync(userId, organizationId: null, CancellationToken.None);
        return grant.Permissions.ToHashSet(StringComparer.Ordinal);
    }

    internal static bool CanUseThePanel(IReadOnlySet<string> permissions) => permissions.Any(SystemPermissions.Global.ContainsKey);
}

internal sealed class AdminAccessRequirement : IAuthorizationRequirement;

/// <summary>
/// Checks <see cref="AdminAccess.Policy"/> against the database on every check, in a scope of its own, so it holds in
/// a long-lived circuit as well as on each request.
/// </summary>
internal sealed class AdminAccessHandler(IServiceScopeFactory scopeFactory) : AuthorizationHandler<AdminAccessRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminAccessRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        // The panel stays closed while an administrator acts as someone else, whatever that user's own access.
        if (context.User.HasClaim(claim => claim.Type == AdminAccess.ActorClaim))
        {
            context.Fail(new AuthorizationFailureReason(this, AdminAccess.Impersonating));
            return;
        }

        if (!AdminAccess.CanUseThePanel(await AdminAccess.PermissionsOfAsync(scopeFactory, context.User)))
        {
            context.Fail(new AuthorizationFailureReason(this, "The user holds no system permission for the installation."));
            return;
        }

        if (!context.User.HasClaim(AdminAccess.MethodClaim, AdminAccess.MultiFactorMethod))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<MfaPolicy>().IsRequiredAsync(userId, CancellationToken.None))
            {
                context.Fail(new AuthorizationFailureReason(this, AdminAccess.SecondFactorNeeded));
                return;
            }
        }

        context.Succeed(requirement);
    }
}
