using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Organizations;
using Kimlik.Contracts.Account;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages.Account;

/// <summary>The user's organizations and the invitations to their address, and creating an organization.</summary>
public sealed class OrganizationsModel(
    MyOrganizations organizations, MyInvitations invitations, AccountErrorMessages errorMessages, IOptions<OrganizationOptions> options) : AccountPageModel
{
    [BindProperty]
    public CreateOrganizationInput Input { get; set; } = new();

    public IReadOnlyList<MyOrganizationResponse> Organizations { get; private set; } = [];

    public IReadOnlyList<MyInvitationResponse> Invitations { get; private set; } = [];

    public bool CanCreate => options.Value.UsersCanCreate;

    public string? ErrorMessage { get; private set; }

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var created = await organizations.CreateAsync(UserId, new CreateMyOrganizationRequest { Name = Input.Name.Trim(), Slug = Input.Slug.Trim() }, cancellationToken);
            if (created.IsSuccess)
            {
                return RedirectToPage("/Account/Organization", new { id = created.Value.Id });
            }

            ModelState.AddModelError(errorMessages.FieldFor(created.Error), errorMessages.For(created.Error));
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAcceptAsync(Guid id, CancellationToken cancellationToken)
    {
        var accepted = await invitations.AcceptAsync(UserId, id, cancellationToken);
        if (accepted.IsSuccess)
        {
            return RedirectToPage("/Account/Organization", new { id = accepted.Value });
        }

        ErrorMessage = errorMessages.For(accepted.Error);
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeclineAsync(Guid id, CancellationToken cancellationToken)
    {
        // An invitation that is already gone simply no longer shows up.
        await invitations.DeclineAsync(UserId, id, cancellationToken);
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Organizations = await organizations.ListAsync(UserId, cancellationToken);
        Invitations = await invitations.ListAsync(UserId, cancellationToken);
    }
}

public sealed class CreateOrganizationInput
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
