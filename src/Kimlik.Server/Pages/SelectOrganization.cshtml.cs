using Kimlik.Application.Organizations;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Kimlik.Server.Pages;

/// <summary>
/// Lets a signed-in user choose the organization to sign in to, for clients that require one. The choice goes
/// back to the authorization request as its <c>organization</c> parameter.
/// </summary>
[Authorize]
public sealed class SelectOrganizationModel(UserManager<User> userManager, UserOrganizations userOrganizations) : PageModel
{
    private const string AuthorizationPath = "/connect/authorize";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IReadOnlyList<OrganizationResponse> Organizations { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!IsAuthorizationRequest(ReturnUrl))
        {
            return BadRequest();
        }

        Organizations = await userOrganizations.ListAsync(CurrentUserId(), cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!IsAuthorizationRequest(ReturnUrl))
        {
            return BadRequest();
        }

        var organization = await userOrganizations.FindAsync(CurrentUserId(), organizationId.ToString(), cancellationToken);
        if (organization.IsFailure)
        {
            Organizations = await userOrganizations.ListAsync(CurrentUserId(), cancellationToken);
            return Page();
        }

        return LocalRedirect(QueryHelpers.AddQueryString(ReturnUrl, KimlikParameters.Organization, organization.Value.Id.ToString()));
    }

    private Guid CurrentUserId() => Guid.Parse(userManager.GetUserId(User)!);

    /// <summary>Only an authorization request on this server is a valid place to return to.</summary>
    private bool IsAuthorizationRequest([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? url) =>
        url is not null && Url.IsLocalUrl(url) && url.StartsWith($"{Request.PathBase}{AuthorizationPath}?", StringComparison.Ordinal);
}
