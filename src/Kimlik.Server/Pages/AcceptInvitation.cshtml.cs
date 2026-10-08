using Kimlik.Application.Organizations;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages;

/// <summary>
/// The page an invitation email links to. People sign in or create an account with the invited address, then
/// join the organization.
/// </summary>
public sealed class AcceptInvitationModel(
    FindInvitationHandler findInvitation,
    AcceptInvitationHandler acceptInvitation,
    UserManager<User> userManager) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public InvitationPreview? Invitation { get; private set; }

    /// <summary>The signed-in user's address, when it is not the invited one.</summary>
    public string? OtherEmail { get; private set; }

    public bool SignedIn { get; private set; }

    public bool Joined { get; private set; }

    public string ReturnUrl => $"{Request.PathBase}{Request.Path}{Request.QueryString}";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (Invitation is null || !SignedIn || OtherEmail is not null)
        {
            return Page();
        }

        var accepted = await acceptInvitation.HandleAsync(Guid.Parse(userManager.GetUserId(User)!), Token!, cancellationToken);
        Joined = accepted.IsSuccess;
        if (!Joined)
        {
            Invitation = null;
        }

        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Token))
        {
            return;
        }

        var preview = await findInvitation.HandleAsync(Token, cancellationToken);
        Invitation = preview.IsSuccess ? preview.Value : null;

        SignedIn = User.Identity?.IsAuthenticated == true;
        if (Invitation is not null && SignedIn && await userManager.GetUserAsync(User) is { } user
            && !string.Equals(userManager.NormalizeEmail(user.Email), userManager.NormalizeEmail(Invitation.Email), StringComparison.Ordinal))
        {
            OtherEmail = user.Email;
        }
    }
}
