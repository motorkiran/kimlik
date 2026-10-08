using System.Net;
using Kimlik.Server.Tests.Accounts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Kimlik.Server.Tests.Security;

public sealed class RateLimitingTests(KimlikServerFixture server)
{
    [Fact]
    public async Task SignIn_IsLimitedPerAddress_WithTheReasonOnThePage()
    {
        await using var limited = await StartWithLimitAsync("SignInsPerMinute", 2);
        using var browser = new Browser(limited);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await browser.SignInAsync("someone@example.com", "not the password at all");
            allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var rejected = await browser.SignInAsync("someone@example.com", "not the password at all");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Browser.ReadPageAsync(rejected, HttpStatusCode.TooManyRequests)).Text.ShouldContain("Too many attempts. Wait a minute and try again.");
    }

    [Fact]
    public async Task TokenEndpoint_IsLimitedPerAddress_WithRetryAfter()
    {
        await using var limited = await StartWithLimitAsync("ProtocolRequestsPerMinute", 2);
        using var client = limited.CreateClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await PostClientCredentialsAsync(client);
            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        using var rejected = await PostClientCredentialsAsync(client);
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task PasswordResetEmails_AreLimitedPerAccount_EvenAcrossRequests()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);

        for (var request = 0; request < 3; request++)
        {
            var page = await browser.GetPageAsync("/forgot-password");
            using var response = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Email"] = user.Email });
            (await Browser.ReadPageAsync(response)).Text.ShouldContain("If an account exists for that address");
        }

        await server.Emails.WaitForAsync(user.Email, "Reset your Kimlik password");
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        server.Emails.SentTo(user.Email).Count().ShouldBe(1);
    }

    private async Task<WebApplicationFactory<Program>> StartWithLimitAsync(string limit, int permitsPerMinute)
    {
        var limited = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"Kimlik:RateLimits:{limit}"] = permitsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture) })));

        await KimlikServerFixture.WaitUntilReadyAsync(limited);
        return limited;
    }

    private static Task<HttpResponseMessage> PostClientCredentialsAsync(HttpClient client) =>
        client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent([new("grant_type", "client_credentials"), new("client_id", "unknown"), new("client_secret", "unknown")]),
            TestContext.Current.CancellationToken);
}
