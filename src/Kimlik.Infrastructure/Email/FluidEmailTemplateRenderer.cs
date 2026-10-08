using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using Fluid;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Branding;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Email;

/// <summary>
/// Renders the email templates embedded in this assembly (<c>Email/Templates</c>). A template sets three
/// captured blocks: <c>subject</c>, <c>html</c> (wrapped in the shared HTML layout) and <c>text</c>.
/// </summary>
internal sealed class FluidEmailTemplateRenderer(IOptions<BrandingOptions> branding) : IEmailTemplateRenderer
{
    private const string DefaultLanguage = "en";
    private const string ResourcePrefix = "Kimlik.Infrastructure.Email.Templates.";

    private static readonly FluidParser Parser = new();
    private static readonly ConcurrentDictionary<string, IFluidTemplate?> Templates = new(StringComparer.Ordinal);

    public async Task<RenderedEmail> RenderAsync(
        string templateName, string? language, IReadOnlyDictionary<string, object?> model, CancellationToken cancellationToken)
    {
        var source = GetTemplate($"{templateName}.{NormalizeLanguage(language)}.liquid")
            ?? GetTemplate($"{templateName}.{DefaultLanguage}.liquid")
            ?? throw new InvalidOperationException($"The email template '{templateName}' does not exist.");

        var values = new Dictionary<string, object?>(model, StringComparer.Ordinal)
        {
            ["product"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = branding.Value.ProductName,
                ["color"] = branding.Value.PrimaryColor,
                ["logo_url"] = branding.Value.LogoUrl?.AbsoluteUri,
            },
        };

        // Rendered twice: HTML-encoded for the HTML part, verbatim for the subject and the plain text part.
        // The contexts are not isolated, so the captured blocks are still readable after rendering.
        var htmlContext = new TemplateContext(values);
        await source.RenderAsync(htmlContext, HtmlEncoder.Default, isolateContext: false);
        var htmlBody = htmlContext.GetValue("html").ToStringValue();

        var textContext = new TemplateContext(values);
        await source.RenderAsync(textContext, NullEncoder.Default, isolateContext: false);

        var layoutContext = new TemplateContext(values);
        layoutContext.SetValue("content", htmlBody);
        var html = await GetTemplate("_layout.html.liquid")!.RenderAsync(layoutContext, HtmlEncoder.Default);

        return new RenderedEmail(
            textContext.GetValue("subject").ToStringValue().Trim(),
            html,
            textContext.GetValue("text").ToStringValue().Trim());
    }

    private static string NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language.Split('-')[0].ToUpperInvariant() switch
        {
            "TR" => "tr",
            _ => DefaultLanguage,
        };

    private static IFluidTemplate? GetTemplate(string name) => Templates.GetOrAdd(name, static resourceName =>
    {
        using var stream = typeof(FluidEmailTemplateRenderer).Assembly.GetManifestResourceStream(ResourcePrefix + resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return Parser.TryParse(reader.ReadToEnd(), out var template, out var error)
            ? template
            : throw new InvalidOperationException($"The email template '{resourceName}' is invalid: {error}");
    });
}
