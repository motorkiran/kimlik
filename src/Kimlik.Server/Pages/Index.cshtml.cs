using System.Security.Claims;
using Kimlik.Application.Accounts;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages;

public sealed class IndexModel(IOptions<AccountOptions> accounts) : PageModel
{
    public string? Email { get; private set; }

    public bool CanSignUp => accounts.Value.Registration == RegistrationMode.Open;

    public void OnGet() => Email = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.Email) : null;
}
