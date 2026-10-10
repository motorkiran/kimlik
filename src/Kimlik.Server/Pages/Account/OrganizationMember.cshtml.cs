using Kimlik.Application.Organizations;
using Kimlik.Application.Roles;
using Kimlik.Contracts.Management;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages.Account;

/// <summary>A member of one of the user's organizations, whose roles the user changes or whom they remove, as their roles allow.</summary>
public sealed class OrganizationMemberModel(
    OrganizationSelfService selfService,
    ListRolesHandler roles,
    AccountErrorMessages errorMessages,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    /// <summary>The keys of the roles the member is to hold.</summary>
    [BindProperty]
    public List<string> Selected { get; set; } = [];

    public Guid OrganizationId { get; private set; }

    public MemberResponse Member { get; private set; } = null!;

    public IReadOnlyList<RoleResponse> Roles { get; private set; } = [];

    public string? Message { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        await LoadAsync(id, userId, cancellationToken) ?? Page();

    public async Task<IActionResult> OnPostRolesAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var changed = await selfService.SetMemberRolesAsync(UserId, id, userId, new SetRolesRequest { Roles = Selected }, cancellationToken);
        if (changed.IsSuccess)
        {
            Message = localizer["The roles have been saved."];
        }
        else
        {
            ErrorMessage = errorMessages.For(changed.Error);
        }

        return await LoadAsync(id, userId, cancellationToken) ?? Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var removed = await selfService.RemoveMemberAsync(UserId, id, userId, cancellationToken);
        if (removed.IsSuccess)
        {
            return userId == UserId ? RedirectToPage("/Account/Organizations") : RedirectToPage("/Account/Organization", new { id });
        }

        ErrorMessage = errorMessages.For(removed.Error);
        return await LoadAsync(id, userId, cancellationToken) ?? Page();
    }

    /// <summary>Loads the member; a result instead when the page cannot show them.</summary>
    private async Task<IActionResult?> LoadAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var member = await selfService.GetMemberAsync(UserId, id, userId, cancellationToken);
        if (member.IsFailure)
        {
            // Members who cannot see the members learn no more than others.
            return NotFound();
        }

        OrganizationId = id;
        Member = member.Value;
        Selected = [.. Member.Roles];
        var listed = await roles.HandleAsync(new ListRolesQuery(null, RoleScope.Organization, null, 200), cancellationToken);
        Roles = listed.IsSuccess ? listed.Value.Items : [];
        return null;
    }
}
