using System.Net;
using System.Text;
using Kimlik.Server.Captcha;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Accounts;

/// <summary>A CAPTCHA, through a fake Turnstile, on the forms bots go for.</summary>
public sealed class CaptchaTests(KimlikServerFixture server)
{
    private const string Answer = "cf-turnstile-response";

    [Fact]
    public async Task SignUp_TakesAnAnswerTheProviderAccepts()
    {
        var turnstile = new FakeTurnstile();
        await using var kimlik = WithCaptcha(turnstile);
        using var browser = new Browser(kimlik);

        using var response = await browser.GetAsync("/signup");
        var page = await Browser.ReadPageAsync(response);
        page.Document.QuerySelector(".cf-turnstile")!.GetAttribute("data-sitekey").ShouldBe("site-key");
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldContain("script-src 'nonce-");
        policy.ShouldContain("https://challenges.cloudflare.com");
        policy.ShouldContain("frame-src https://challenges.cloudflare.com");

        var refused = await Browser.ReadPageAsync(await SignUpAsync(browser, answer: null));
        refused.Text.ShouldContain("Confirm that you are not a robot.");
        var wrong = await Browser.ReadPageAsync(await SignUpAsync(browser, answer: "made up"));
        wrong.Text.ShouldContain("Confirm that you are not a robot.");
        using var accepted = await SignUpAsync(browser, answer: FakeTurnstile.Good);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        turnstile.Checked.ShouldContain(form => form.Contains("secret=secret-key", StringComparison.Ordinal) && form.Contains($"response={FakeTurnstile.Good}", StringComparison.Ordinal));

        turnstile.Down = true;
        using var duringAnOutage = await SignUpAsync(browser, answer: "anything");
        duringAnOutage.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task SignIn_GuardsTheCodeButNotThePassword_ByDefault()
    {
        await using var kimlik = WithCaptcha(new FakeTurnstile());
        var user = await server.CreateUserAsync();
        using var browser = new Browser(kimlik);

        var page = await browser.GetPageAsync("/signin");
        using var code = await browser.SubmitAsync(
            page, new Dictionary<string, string> { ["Input.Email"] = user.Email }, action: page.Document.QuerySelector("#email-code")!.GetAttribute("formaction"));
        (await Browser.ReadPageAsync(code)).Text.ShouldContain("Confirm that you are not a robot.");

        using var signedIn = await browser.SignInAsync(user.Email, user.Password);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task PagesWithoutTheWidget_KeepTheirStrictPolicy()
    {
        using var browser = new Browser(server);

        using var response = await browser.GetAsync("/signup");

        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("script-src 'none'");
    }

    private WebApplicationFactory<Program> WithCaptcha(FakeTurnstile turnstile) => server.WithWebHostBuilder(builder => builder
        .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kimlik:Captcha:Provider"] = "Turnstile",
            ["Kimlik:Captcha:SiteKey"] = "site-key",
            ["Kimlik:Captcha:SecretKey"] = "secret-key",
        }))
        .ConfigureTestServices(services => services.AddHttpClient(CaptchaVerifier.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => turnstile)));

    private static async Task<HttpResponseMessage> SignUpAsync(Browser browser, string? answer)
    {
        var page = await browser.GetPageAsync("/signup");
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = $"new-{Guid.NewGuid():N}@example.com",
            ["Input.Password"] = "a passphrase nobody uses",
        };

        if (answer is not null)
        {
            fields[Answer] = answer;
        }

        return await browser.SubmitAsync(page, fields);
    }

    /// <summary>Turnstile's siteverify: it accepts one answer, and fails while it is down.</summary>
    private sealed class FakeTurnstile : HttpMessageHandler
    {
        public const string Good = "a-good-answer";

        private readonly List<string> _checked = [];

        public bool Down { get; set; }

        public IReadOnlyList<string> Checked => _checked;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down)
            {
                throw new HttpRequestException("Turnstile is down.");
            }

            request.RequestUri.ShouldBe(new Uri("https://challenges.cloudflare.com/turnstile/v0/siteverify"));
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            _checked.Add(form);
            var success = form.Contains($"response={Good}", StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{ "success": {{(success ? "true" : "false")}}, "error-codes": [] }""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
