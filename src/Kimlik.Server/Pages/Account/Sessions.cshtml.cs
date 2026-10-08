using Kimlik.Application.Accounts;
using Kimlik.Contracts.Account;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Kimlik.Server.Pages.Account;

/// <summary>The applications the user signed in to, which can each be signed out, or all of them with this browser.</summary>
public sealed class SessionsModel(ApplicationSessions sessions, SignOutService signOut) : AccountPageModel
{
    public IReadOnlyList<SessionResponse> Sessions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Sessions = (await sessions.ListAsync(UserId, cancellationToken)) is { IsSuccess: true } listed ? listed.Value : [];

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        // A session that is already gone simply no longer shows up.
        await sessions.RevokeAsync(UserId, id, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSignOutEverywhereAsync(CancellationToken cancellationToken)
    {
        await sessions.RevokeAllAsync(UserId, cancellationToken);
        await signOut.SignOutAsync(User, cancellationToken);
        return RedirectToPage("/Index");
    }
}
