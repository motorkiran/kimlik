using Kimlik.Application.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages;

public sealed class ForgotPasswordModel(RequestPasswordResetHandler requestPasswordReset) : PageModel
{
    [BindProperty]
    public EmailInput Input { get; set; } = new();

    public bool Sent { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await requestPasswordReset.HandleAsync(new RequestPasswordResetCommand(Input.Email), cancellationToken);

        Sent = true;
        return Page();
    }
}
