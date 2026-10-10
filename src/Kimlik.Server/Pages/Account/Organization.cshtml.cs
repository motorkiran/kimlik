using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Organizations;
using Kimlik.Application.Roles;
using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using RoleScope = Kimlik.Contracts.Management.RoleScope;

namespace Kimlik.Server.Pages.Account;

/// <summary>
/// One of the user's organizations, with what their organization roles allow: its settings, members and invitations, as
/// the Account API offers them; and leaving it.
/// </summary>
public sealed class OrganizationModel(
    MyOrganizations organizations,
    OrganizationSelfService selfService,
    ListRolesHandler roles,
    AccountErrorMessages errorMessages,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    private const int PageSize = 100;

    [BindProperty]
    public OrganizationSettingsInput Settings { get; set; } = new();

    [BindProperty]
    public InviteInput Invite { get; set; } = new();

    /// <summary>Where the list of members goes on from, as the previous page left it.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Cursor { get; set; }

    public MyOrganizationResponse Organization { get; private set; } = null!;

    public IReadOnlyList<MemberResponse> Members { get; private set; } = [];

    public string? NextCursor { get; private set; }

    public IReadOnlyList<InvitationResponse> Invitations { get; private set; } = [];

    /// <summary>The organization roles members can hold.</summary>
    public IReadOnlyList<RoleResponse> Roles { get; private set; } = [];

    public string? Message { get; private set; }

    public string? ErrorMessage { get; private set; }

    public bool CanChangeSettings => Has(SystemPermissions.OrganizationSettingsWrite);

    public bool CanSeeMembers => Has(SystemPermissions.OrganizationMembersRead);

    public bool CanManageMembers => Has(SystemPermissions.OrganizationMembersWrite);

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnPostSettingsAsync(Guid id, CancellationToken cancellationToken)
    {
        if (OnlyValid(nameof(Settings)))
        {
            var updated = await selfService.UpdateAsync(
                UserId, id, new UpdateMyOrganizationRequest { Name = Settings.Name.Trim(), Slug = Settings.Slug.Trim() }, cancellationToken);
            if (updated.IsSuccess)
            {
                Message = localizer["The organization has been saved."];
                return await ShowAsync(id, cancellationToken);
            }

            Report(updated.Error, field => $"{nameof(Settings)}.{field[(field.IndexOf('.') + 1)..]}");
        }

        return await ShowAsync(id, cancellationToken, keepSettings: true);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, string? confirmation, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!string.Equals(confirmation?.Trim(), Organization.Slug, StringComparison.Ordinal))
        {
            ErrorMessage = localizer["Type the organization's short name to delete it."];
            return Page();
        }

        var deleted = await selfService.DeleteAsync(UserId, id, cancellationToken);
        if (deleted.IsSuccess)
        {
            return RedirectToPage("/Account/Organizations");
        }

        ErrorMessage = errorMessages.For(deleted.Error);
        return Page();
    }

    public async Task<IActionResult> OnPostInviteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (OnlyValid(nameof(Invite)))
        {
            var invited = await selfService.InviteAsync(
                UserId, id, new CreateInvitationRequest { Email = Invite.Email.Trim(), Roles = Invite.Roles }, cancellationToken);
            if (invited.IsSuccess)
            {
                Message = localizer["We sent an invitation to {0}.", invited.Value.Email];
                ModelState.Clear();
                Invite = new InviteInput();
                return await ShowAsync(id, cancellationToken);
            }

            Report(invited.Error, _ => string.Empty);
        }

        return await ShowAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnPostResendAsync(Guid id, Guid invitationId, CancellationToken cancellationToken)
    {
        var resent = await selfService.ResendInvitationAsync(UserId, id, invitationId, cancellationToken);
        return await AfterAsync(id, resent.IsSuccess ? null : resent.Error, localizer["The invitation has been sent again."], cancellationToken);
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, Guid invitationId, CancellationToken cancellationToken)
    {
        var revoked = await selfService.RevokeInvitationAsync(UserId, id, invitationId, cancellationToken);
        return await AfterAsync(id, revoked.IsSuccess ? null : revoked.Error, localizer["The invitation has been revoked."], cancellationToken);
    }

    public async Task<IActionResult> OnPostLeaveAsync(Guid id, CancellationToken cancellationToken)
    {
        var left = await organizations.LeaveAsync(UserId, id, cancellationToken);
        return left.IsSuccess ? RedirectToPage("/Account/Organizations") : await AfterAsync(id, left.Error, done: string.Empty, cancellationToken);
    }

    private bool Has(string permission) => Organization.Permissions.Contains(permission, StringComparer.Ordinal);

    /// <summary>Whether the form with <paramref name="prefix"/> is valid; the page's other forms were not sent.</summary>
    private bool OnlyValid(string prefix)
    {
        foreach (var key in ModelState.Keys.Where(key => !key.StartsWith(prefix + ".", StringComparison.Ordinal)).ToList())
        {
            ModelState.Remove(key);
        }

        return ModelState.IsValid;
    }

    private void Report(Error error, Func<string, string> fieldOf)
    {
        var field = errorMessages.FieldFor(error);
        ModelState.AddModelError(field.Length == 0 ? string.Empty : fieldOf(field), errorMessages.For(error));
    }

    private async Task<IActionResult> AfterAsync(Guid id, Error? error, string done, CancellationToken cancellationToken)
    {
        if (error is null)
        {
            Message = done;
        }
        else
        {
            ErrorMessage = errorMessages.For(error);
        }

        return await ShowAsync(id, cancellationToken);
    }

    private async Task<IActionResult> ShowAsync(Guid id, CancellationToken cancellationToken, bool keepSettings = false) =>
        await LoadAsync(id, cancellationToken, keepSettings) ? Page() : NotFound();

    /// <summary>Loads the organization as the member sees it; <see langword="false"/> when they are not a member.</summary>
    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken, bool keepSettings = false)
    {
        if ((await organizations.ListAsync(UserId, cancellationToken)).SingleOrDefault(organization => organization.Id == id) is not { } organization)
        {
            return false;
        }

        Organization = organization;
        if (!keepSettings)
        {
            Settings = new OrganizationSettingsInput { Name = organization.Name, Slug = organization.Slug };
        }

        if (CanSeeMembers)
        {
            var members = await selfService.ListMembersAsync(UserId, new ListMembersQuery(id, Cursor, PageSize), cancellationToken);
            (Members, NextCursor) = members.IsSuccess ? (members.Value.Items, members.Value.NextCursor) : ([], null);

            var invitations = await selfService.ListInvitationsAsync(UserId, new ListInvitationsQuery(id, null, PageSize), cancellationToken);
            Invitations = invitations.IsSuccess ? [.. invitations.Value.Items.Where(invitation => invitation.Status == InvitationStatus.Pending)] : [];
        }

        if (CanManageMembers)
        {
            var listed = await roles.HandleAsync(new ListRolesQuery(null, RoleScope.Organization, null, 200), cancellationToken);
            Roles = listed.IsSuccess ? listed.Value.Items : [];
        }

        return true;
    }
}

public sealed class OrganizationSettingsInput
{
    [Required(ErrorMessage = "Enter a name.")]
    [StringLength(100, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a short name.")]
    [StringLength(64, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Short name")]
    public string Slug { get; set; } = string.Empty;
}

public sealed class InviteInput
{
    [Required(ErrorMessage = "Enter an email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];
}
