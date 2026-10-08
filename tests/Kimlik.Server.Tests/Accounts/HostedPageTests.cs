using System.Text.RegularExpressions;
using Kimlik.Server.Tests.Oidc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Tests.Accounts;

public sealed class HostedPageTests(KimlikServerFixture server)
{
    [Fact]
    public async Task Pages_FollowTheBrowserLanguage()
    {
        using var browser = new Browser(server, acceptLanguage: "tr-TR,tr;q=0.9");

        var page = await browser.GetPageAsync("/signin");

        page.Document.DocumentElement.GetAttribute("lang").ShouldBe("tr");
        page.Text.ShouldContain("Giriş yap");
        page.Document.Source.Text.ShouldContain("Giriş yap", customMessage: "Non-Latin text is written as UTF-8, not as numeric entities.");
    }

    [Fact]
    public async Task UiLocales_OfTheAuthorizationRequest_WinsOverTheBrowserLanguage()
    {
        var client = await server.CreateWebClientAsync();
        using var browser = new Browser(server, acceptLanguage: "en");

        using var challenge = await browser.GetAsync(new AuthorizationRequest(client.ClientId).Url + "&ui_locales=tr");
        var page = await browser.GetPageAsync(challenge.Headers.Location!.ToString());

        page.Text.ShouldContain("Tekrar hoş geldiniz.");
    }

    [Fact]
    public async Task Pages_SendAStrictContentSecurityPolicy()
    {
        using var browser = new Browser(server);

        using var response = await browser.GetAsync("/signup");

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldContain("script-src 'none'");
        policy.ShouldContain("frame-ancestors 'none'");
        policy.ShouldMatch("style-src 'self' 'nonce-[A-Za-z0-9+/=]+'");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
    }

    [Fact]
    public async Task OnlyPagesThatNeedIt_RunScripts_WithTheRequestsNonce()
    {
        using var browser = new Browser(server);

        using var signIn = await browser.GetAsync("/signin");
        using var signUp = await browser.GetAsync("/signup");

        var policy = signIn.Headers.GetValues("Content-Security-Policy").Single();
        var nonce = Regex.Match(policy, "script-src 'nonce-([A-Za-z0-9+/=]+)'");
        nonce.Success.ShouldBeTrue();
        (await Browser.ReadPageAsync(signIn)).Document.QuerySelector("script[src*=passkeys]")!.GetAttribute("nonce").ShouldBe(nonce.Groups[1].Value);
        signUp.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("script-src 'none'");
    }

    [Fact]
    public async Task Browsers_AreToldToKeepToHttps_WhenKimlikRequiresIt()
    {
        // This host shares the database, so it may send other tests' emails: their links stay on localhost. Browsers are
        // never told to keep localhost to HTTPS, so the request goes to another name.
        await using var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kimlik:Server:PublicUrl"] = "https://localhost/",
                ["Kimlik:Server:RequireHttps"] = "true",
            })));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        using var https = kimlik.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://id.example.test/") });

        using var response = await https.GetAsync("/signin", TestContext.Current.CancellationToken);

        response.Headers.GetValues("Strict-Transport-Security").Single().ShouldBe("max-age=31536000");
    }

    [Fact]
    public async Task CustomStylesheet_IsLoadedAfterThePagesOwn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kimlik-{Guid.NewGuid():N}.css");
        await File.WriteAllTextAsync(path, ".card { border-radius: 0; }", TestContext.Current.CancellationToken);
        try
        {
            await using var kimlik = WithStylesheet(path);
            using var browser = new Browser(kimlik);

            var page = await browser.GetPageAsync("/signin");
            var href = page.Document.QuerySelectorAll("link[rel=stylesheet]")[^1].GetAttribute("href")!;
            using var stylesheet = await browser.GetAsync(href);

            href.ShouldStartWith("/branding.css?v=");
            stylesheet.Content.Headers.ContentType!.MediaType.ShouldBe("text/css");
            (await stylesheet.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(".card { border-radius: 0; }");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingStylesheet_StopsKimlikFromStarting()
    {
        await using var kimlik = WithStylesheet(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.css"));

        Should.Throw<OptionsValidationException>(() => kimlik.CreateClient()).Message.ShouldContain("StylesheetPath");
    }

    private WebApplicationFactory<Program> WithStylesheet(string path) =>
        server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kimlik:Branding:StylesheetPath"] = path })));
}
