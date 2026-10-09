using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Accounts;

/// <summary>New passwords that appear in known data breaches are refused, through a fake Pwned Passwords service.</summary>
public sealed class BreachedPasswordTests(KimlikServerFixture server)
{
    private const string Breached = "a passphrase everyone uses";

    [Fact]
    public async Task BreachedPassword_IsRefused_AndAnOutageDoesNotBlockSignUp()
    {
        var pwnedPasswords = new FakePwnedPasswords(Breached);
        await using var kimlik = server.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kimlik:Accounts:BreachedPasswordCheck"] = "true" }))
            .ConfigureTestServices(services =>
                services.AddHttpClient("Kimlik.PwnedPasswords").ConfigurePrimaryHttpMessageHandler(() => pwnedPasswords)));
        using var browser = new Browser(kimlik);

        var refused = await Browser.ReadPageAsync(await SignUpAsync(browser, Breached));
        refused.Text.ShouldContain("This password has appeared in a data breach.");
        pwnedPasswords.Requested.ShouldHaveSingleItem().ShouldBe($"/range/{Hash(Breached)[..5]}");

        using var accepted = await SignUpAsync(browser, "a passphrase nobody uses");
        accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        pwnedPasswords.Down = true;
        using var withoutCheck = await SignUpAsync(browser, Breached);
        withoutCheck.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private static async Task<HttpResponseMessage> SignUpAsync(Browser browser, string password)
    {
        var page = await browser.GetPageAsync("/signup");
        return await browser.SubmitAsync(page, new Dictionary<string, string>
        {
            ["Input.Email"] = $"new-{Guid.NewGuid():N}@example.com",
            ["Input.Password"] = password,
        });
    }

#pragma warning disable CA5350 // Pwned Passwords is keyed by SHA-1.
    private static string Hash(string password) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350

    /// <summary>Answers range requests with the breached password among padding, or fails while it is down.</summary>
    private sealed class FakePwnedPasswords(string breached) : HttpMessageHandler
    {
        private readonly List<string> _requested = [];

        public bool Down { get; set; }

        public IReadOnlyList<string> Requested => _requested;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down)
            {
                throw new HttpRequestException("The service is down.");
            }

            _requested.Add(request.RequestUri!.AbsolutePath);
            var hash = Hash(breached);
            var body = request.RequestUri.AbsolutePath.EndsWith(hash[..5], StringComparison.Ordinal)
                ? $"0018A45C4D1DEF81644B54AB7F969B88D65:0\r\n{hash[5..]}:42\r\n00D4F6E8FA6EECAD2A3AA415EEC418D38EC:0"
                : "0018A45C4D1DEF81644B54AB7F969B88D65:0";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
