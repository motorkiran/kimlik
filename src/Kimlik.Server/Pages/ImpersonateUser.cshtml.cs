using Kimlik.Admin.Security;
using Kimlik.Application.Access;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Kimlik.Server.Admin;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages;

/// <summary>
/// Where an administrator starts acting as a user, for support, from the user's page in the admin panel: it asks first,
/// then signs the browser in as them (see <see cref="SignInFlow.ImpersonateAsync"/>). It takes
/// <c>kimlik.users:impersonate</c>, and works only for active users whose access to Kimlik the administrator holds too.
/// </summary>
[Authorize(Policy = AdminAccess.Policy)]
public sealed class ImpersonateUserModel(
    UserManager<User> userManager,
    SignInFlow signInFlow,
    AdminSession session,
    AdminOperations operations,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    private User? _user;

    public Guid Id { get; private set; }

    public string? Email => _user?.Email;

    /// <summary>Why the administrator cannot act as this user, if they cannot.</summary>
    public string? Refusal { get; private set; }

    public static int LifetimeInMinutes => (int)SignInFlow.ImpersonationLifetime.TotalMinutes;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        await FindAsync(id, cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await FindAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (Refusal is not null)
        {
            return Page();
        }

        await signInFlow.ImpersonateAsync(_user!, User, cancellationToken);
        return RedirectToPage("/Account/Index");
    }

    /// <summary>Finds the user, and whether the administrator may act as them; nothing on an instance without the panel.</summary>
    private async Task<bool> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        Id = id;
        if (!configuration.IsAdminPanelEnabled() || await userManager.FindByIdAsync(id.ToString()) is not { } user)
        {
            return false;
        }

        _user = user;
        await session.LoadAsync(HttpContext, scopeFactory);
        Refusal = !session.Has(SystemPermissions.UsersImpersonate) ? localizer["Your roles do not allow this."].Value
            : user.Id == session.UserId ? localizer["This is your own account."].Value
            : !user.CanSignIn ? localizer["Only active users can be signed in as."].Value
            : (await operations.RunAsync<AccessGuard, Result>(
                SystemPermissions.UsersImpersonate, guard => guard.EnsureCanManageUserAsync(id, cancellationToken))).IsFailure
                ? localizer["This user has access to Kimlik that you do not have."].Value
            : null;
        return true;
    }
}
