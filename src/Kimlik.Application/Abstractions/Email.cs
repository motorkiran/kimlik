namespace Kimlik.Application.Abstractions;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

public sealed record RenderedEmail(string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public interface IEmailTemplateRenderer
{
    /// <summary>Renders a template in the given language, falling back to English when there is no translation.</summary>
    Task<RenderedEmail> RenderAsync(string templateName, string? language, IReadOnlyDictionary<string, object?> model, CancellationToken cancellationToken);
}
