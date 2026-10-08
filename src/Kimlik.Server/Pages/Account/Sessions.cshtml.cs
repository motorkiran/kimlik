using Kimlik.Application.Accounts;
using Kimlik.Contracts.Account;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Kimlik.Server.Pages.Account;

/// <summary>The applications the user signed in to, which can each be signed out, or all of them with this browser.</summary>
public sealed class SessionsModel(MyAccount account, SignOutService signOut) : AccountPageModel
{
    public IReadOnlyList<SessionResponse> Sessions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => Sessions = await account.ListSessionsAsync(UserId, cancellationToken);

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        // A session that is already gone simply no longer shows up.
        await account.RevokeSessionAsync(UserId, id, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSignOutEverywhereAsync(CancellationToken cancellationToken)
    {
        await account.RevokeAllSessionsAsync(UserId, cancellationToken);
        await signOut.SignOutAsync(User, cancellationToken);
        return RedirectToPage("/Index");
    }
}
