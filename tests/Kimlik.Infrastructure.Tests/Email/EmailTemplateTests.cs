using Kimlik.Application.Branding;
using Kimlik.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Tests.Email;

public sealed class EmailTemplateTests
{
    private readonly FluidEmailTemplateRenderer _renderer = new(Options.Create(new BrandingOptions { ProductName = "Acme", PrimaryColor = "#123456" }));

    [Theory]
    [InlineData("email-verification")]
    [InlineData("password-reset")]
    [InlineData("already-registered")]
    public async Task EveryTemplate_RendersSubjectHtmlAndText_InBothLanguages(string template)
    {
        foreach (var language in new[] { "en", "tr" })
        {
            var email = await RenderAsync(template, language, "Ada");

            email.Subject.ShouldContain("Acme");
            email.HtmlBody.ShouldContain("https://id.example.com/link?token=a%2Bb");
            email.HtmlBody.ShouldContain("#123456");
            email.TextBody.ShouldContain("https://id.example.com/link?token=a%2Bb");
            email.TextBody.ShouldNotContain("<");
        }
    }

    [Fact]
    public async Task UnknownLanguage_FallsBackToEnglish()
    {
        var email = await RenderAsync("password-reset", "de-DE", "Ada");

        email.Subject.ShouldBe("Reset your Acme password");
    }

    [Fact]
    public async Task Values_AreHtmlEncodedInHtml_AndKeptVerbatimInText()
    {
        var email = await RenderAsync("email-verification", "en", "<script>alert(1)</script>");

        email.HtmlBody.ShouldNotContain("<script>");
        email.HtmlBody.ShouldContain("&lt;script&gt;");
        email.TextBody.ShouldContain("Hi <script>alert(1)</script>,");
    }

    private Task<Application.Abstractions.RenderedEmail> RenderAsync(string template, string language, string name) =>
        _renderer.RenderAsync(template, language, new Dictionary<string, object?>
        {
            ["name"] = name,
            ["email"] = "ada@example.com",
            ["link"] = "https://id.example.com/link?token=a%2Bb",
        }, TestContext.Current.CancellationToken);
}
