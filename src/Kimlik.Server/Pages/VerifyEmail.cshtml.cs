using Kimlik.Application.Accounts;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages;

/// <summary>The landing page of the verification link in the email.</summary>
public sealed class VerifyEmailModel(ConfirmEmailHandler confirmEmail) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool Succeeded { get; private set; }

    public async Task OnGetAsync(Guid userId, string? token, CancellationToken cancellationToken)
    {
        if (!AccountLinks.IsLocalUrl(ReturnUrl))
        {
            ReturnUrl = null;
        }

        Succeeded = token is { Length: > 0 }
            && (await confirmEmail.HandleAsync(new ConfirmEmailCommand(userId, token), cancellationToken)).IsSuccess;
    }
}
