using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Accounts;

internal sealed record TestUser(Guid Id, string Email, string Password);

internal static class TestUsers
{
    public const string Password = "correct horse battery staple";

    /// <summary>Creates an account directly, bypassing sign-up, with a unique email address.</summary>
    public static Task<TestUser> CreateUserAsync(this KimlikServerFixture server, bool emailConfirmed = true) =>
        server.WithServicesAsync(async services =>
        {
            var email = $"user-{Guid.NewGuid():N}@example.com";
            var user = User.Create(email, "Ada", "Lovelace", "en", DateTimeOffset.UtcNow);
            user.EmailConfirmed = emailConfirmed;

            var result = await services.GetRequiredService<UserManager<User>>().CreateAsync(user, Password);
            result.Succeeded.ShouldBeTrue(string.Join(", ", result.Errors.Select(error => error.Code)));

            return new TestUser(user.Id, email, Password);
        });

    /// <summary>Signs in through the hosted sign-in page.</summary>
    public static async Task<HttpResponseMessage> SignInAsync(this Browser browser, string email, string password, string? returnUrl = null)
    {
        var page = await browser.GetPageAsync(returnUrl is null ? "/signin" : $"/signin?ReturnUrl={Uri.EscapeDataString(returnUrl)}");
        return await browser.SubmitAsync(page, new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });
    }
}
