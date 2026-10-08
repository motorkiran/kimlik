using Kimlik.Application.Accounts;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages.Account;

/// <summary>
/// The user's passkeys: added through the browser's ceremony, renamed and removed. Adding one takes a recent sign-in,
/// so that someone holding a stolen session cannot plant a passkey of their own.
/// </summary>
[RunsScripts]
public sealed class PasskeysModel(
    UserManager<User> userManager,
    UserPasskeys passkeys,
    PasskeyCeremonies ceremonies,
    TimeProvider timeProvider,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    /// <summary>How recent the sign-in must be to add a passkey.</summary>
    public static readonly TimeSpan RecentSignIn = TimeSpan.FromMinutes(10);

    public IReadOnlyList<PasskeyResponse> Passkeys { get; private set; } = [];

    public bool SignedInRecently { get; private set; }

    public bool CanAdd => Passkeys.Count < UserPasskeys.MaxPerUser;

    public string? Message { get; private set; }

    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public string? Name { get; set; }

    /// <summary>The authenticator's answer, as the browser serializes it.</summary>
    [BindProperty]
    public string? Credential { get; set; }

    /// <summary>The protected state of the ceremony, as it went out with the options.</summary>
    [BindProperty]
    public string? State { get; set; }

    /// <summary>Where to continue once the passkey is added, as after the offer that follows a sign-in.</summary>
    [BindProperty]
    public string? ReturnUrl { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>The options for adding a passkey, with the state to post back; only after a recent sign-in.</summary>
    public async Task<IActionResult> OnPostOptionsAsync()
    {
        if (!await IsSignedInRecentlyAsync() || await userManager.GetUserAsync(User) is not { } user)
        {
            return Forbid();
        }

        var challenge = await ceremonies.BeginCreationAsync(user, HttpContext);
        return Content(challenge.ToJson(), "application/json");
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        if (await userManager.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        if (!await IsSignedInRecentlyAsync())
        {
            ErrorMessage = localizer["Sign in again to add a passkey."];
        }
        else if (Credential is not { Length: > 0 } || State is not { Length: > 0 }
            || await ceremonies.CompleteCreationAsync(user, Credential, State, HttpContext) is not { } passkey)
        {
            ErrorMessage = localizer["The passkey could not be added. Try again."];
        }
        else if ((await passkeys.AddAsync(user, passkey, Name, cancellationToken)).IsFailure)
        {
            ErrorMessage = localizer["You have the most passkeys an account can have. Remove one to add another."];
        }
        else if (AccountLinks.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl!);
        }
        else
        {
            Message = localizer["Your passkey has been added."];
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRenameAsync(string id, string? name, CancellationToken cancellationToken)
    {
        await passkeys.RenameAsync(UserId, id, new RenamePasskeyRequest { Name = name ?? string.Empty }, cancellationToken);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(string id, CancellationToken cancellationToken)
    {
        // A passkey that is already gone simply no longer shows up.
        await passkeys.RemoveAsync(UserId, id, cancellationToken);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Passkeys = (await passkeys.ListAsync(UserId)) is { IsSuccess: true } listed ? listed.Value : [];
        SignedInRecently = await IsSignedInRecentlyAsync();
    }

    private async Task<bool> IsSignedInRecentlyAsync() =>
        SignInFlow.SignedInAt(await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme)) is { } signedInAt
        && timeProvider.GetUtcNow() - signedInAt <= RecentSignIn;
}
