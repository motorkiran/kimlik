using Kimlik.Application.Accounts;
using Kimlik.Server.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages;

public sealed class ForgotPasswordModel(
    RequestPasswordResetHandler requestPasswordReset,
    RequestThrottle throttle,
    IStringLocalizer<SharedResource> localizer) : PageModel
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

        if (!throttle.TryAcquire(ThrottledAction.EmailRequest, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ModelState.AddModelError(string.Empty, localizer["Too many attempts. Wait a minute and try again."]);
            return Page();
        }

        await requestPasswordReset.HandleAsync(new RequestPasswordResetCommand(Input.Email), cancellationToken);

        Sent = true;
        return Page();
    }
}
