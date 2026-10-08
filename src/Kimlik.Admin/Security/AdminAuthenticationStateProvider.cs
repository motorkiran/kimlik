using System.Security.Claims;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimlik.Admin.Security;

/// <summary>
/// Checks every minute that the circuit's administrator may still use the panel: the account still signs in, its
/// security stamp is unchanged (no password reset or "sign out everywhere" since), and it still holds a system
/// permission. Their permissions are refreshed on the way; otherwise the circuit signs out.
/// </summary>
internal sealed class AdminAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    AdminSession session,
    IOptions<IdentityOptions> identity) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var principal = authenticationState.User;
        if (principal.FindFirstValue(ClaimTypes.NameIdentifier) is not { } userId)
        {
            return false;
        }

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            if (await users.FindByIdAsync(userId) is not { CanSignIn: true } user
                || principal.FindFirstValue(identity.Value.ClaimsIdentity.SecurityStampClaimType) != await users.GetSecurityStampAsync(user))
            {
                return false;
            }
        }

        var permissions = await AdminAccess.PermissionsOfAsync(scopeFactory, principal);
        if (!AdminAccess.CanUseThePanel(permissions))
        {
            return false;
        }

        session.Refresh(permissions);
        return true;
    }
}

/// <summary>Loads the circuit's <see cref="AdminSession"/> once, from its authentication state and the database.</summary>
internal sealed class AdminSessionLoader(
    AdminSession session,
    AuthenticationStateProvider authentication,
    IServiceScopeFactory scopeFactory,
    Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
{
    public async Task LoadAsync()
    {
        if (session.IsLoaded)
        {
            return;
        }

        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        // The request that started the circuit, for the audit trail; its address is the administrator's.
        var request = httpContextAccessor.HttpContext;
        session.Load(
            userId,
            user.FindFirstValue(ClaimTypes.Email),
            await AdminAccess.PermissionsOfAsync(scopeFactory, user),
            request?.Connection.RemoteIpAddress,
            request?.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent ? userAgent : null);
    }
}
