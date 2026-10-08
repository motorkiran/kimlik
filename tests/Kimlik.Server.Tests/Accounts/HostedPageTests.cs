using Kimlik.Server.Tests.Oidc;

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

        using var response = await browser.GetAsync("/signin");

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldContain("script-src 'none'");
        policy.ShouldContain("frame-ancestors 'none'");
        policy.ShouldMatch("style-src 'self' 'nonce-[A-Za-z0-9+/=]+'");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
    }
}
