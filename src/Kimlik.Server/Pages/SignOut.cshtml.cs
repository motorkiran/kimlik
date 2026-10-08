using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Pages;

public sealed class SignOutModel(SignOutService signOut) : PageModel
{
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Page() : RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await signOut.SignOutAsync(User, cancellationToken);
        return RedirectToPage("/Index");
    }
}
