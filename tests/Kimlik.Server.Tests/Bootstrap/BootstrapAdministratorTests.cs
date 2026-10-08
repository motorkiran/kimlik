using Kimlik.Domain.Access;
using Kimlik.Domain.Users;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Server.Tests.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Bootstrap;

/// <summary>Each test starts Kimlik on a database of its own, as bootstrapping depends on the whole installation.</summary>
public sealed class BootstrapAdministratorTests(KimlikServerFixture server)
{
    private const string Password = "a long bootstrap passphrase";

    [Fact]
    public async Task FirstStart_CreatesAnAdministrator_WhoCanSignIn()
    {
        var database = NewDatabase();
        var email = NewEmail();

        await using var kimlik = await StartAsync(database, email, Password);

        using var browser = new Browser(kimlik);
        using var signIn = await browser.SignInAsync(email, Password);
        signIn.Headers.Location!.ToString().ShouldBe("/signin/passkey?returnUrl=%2F");
        (await AdministratorsAsync(kimlik)).ShouldBe([email]);
    }

    [Fact]
    public async Task LaterStarts_LeaveTheAdministratorsAlone()
    {
        var database = NewDatabase();
        var first = NewEmail();
        await using (await StartAsync(database, first, Password))
        {
        }

        await using var restarted = await StartAsync(database, NewEmail(), Password);

        (await AdministratorsAsync(restarted)).ShouldBe([first]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingAccount_IsPromoted_OnlyWithAVerifiedAddress(bool verified)
    {
        var database = NewDatabase();
        var email = NewEmail();
        await using (var withoutBootstrap = await StartAsync(database, adminEmail: null, adminPassword: null))
        {
            await using var scope = withoutBootstrap.Services.CreateAsyncScope();
            var user = User.Create(email, "Grace", "Hopper", "en", DateTimeOffset.UtcNow);
            user.EmailConfirmed = verified;
            (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAsync(user, TestUsers.Password)).Succeeded.ShouldBeTrue();
        }

        await using var kimlik = await StartAsync(database, email, Password);

        (await AdministratorsAsync(kimlik)).ShouldBe(verified ? [email] : []);
    }

    [Fact]
    public async Task EmailWithoutPassword_StopsStartup()
    {
        var exception = await Should.ThrowAsync<Exception>(() => StartAsync(NewDatabase(), NewEmail(), adminPassword: null));

        exception.ToString().ShouldContain("set both AdminEmail and AdminPassword");
    }

    private async Task<WebApplicationFactory<Program>> StartAsync(string database, string? adminEmail, string? adminPassword)
    {
        var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Kimlik"] = server.ConnectionStringFor(database),
                ["Kimlik:Bootstrap:AdminEmail"] = adminEmail,
                ["Kimlik:Bootstrap:AdminPassword"] = adminPassword,
            })));

        try
        {
            await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
            return kimlik;
        }
        catch
        {
            await kimlik.DisposeAsync();
            throw;
        }
    }

    private static async Task<string?[]> AdministratorsAsync(WebApplicationFactory<Program> kimlik)
    {
        await using var scope = kimlik.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<KimlikDbContext>();

        return await context.UserRoles
            .Where(assignment => context.Roles.Any(role => role.Id == assignment.RoleId && role.Key == SystemRoles.Admin))
            .Join(context.Users, assignment => assignment.UserId, user => user.Id, (_, user) => user.Email)
            .ToArrayAsync(TestContext.Current.CancellationToken);
    }

    private static string NewDatabase() => $"bootstrap_{Guid.NewGuid():N}";

    private static string NewEmail() => $"admin-{Guid.NewGuid():N}@example.com";
}
