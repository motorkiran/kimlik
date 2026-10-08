using Kimlik.Application.Abstractions;
using Kimlik.Application.Branding;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Kimlik.Infrastructure.Email;

/// <summary>Sends email through an SMTP relay with MailKit.</summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options, IOptions<BrandingOptions> branding) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var fromAddress = settings.FromAddress ?? throw new InvalidOperationException("Kimlik:Email:FromAddress is not configured.");
        var host = settings.Smtp.Host ?? throw new InvalidOperationException("Kimlik:Email:Smtp:Host is not configured.");

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName ?? branding.Value.ProductName, fromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(host, settings.Smtp.Port, ToSocketOptions(settings.Smtp.Security), cancellationToken);

        if (!string.IsNullOrEmpty(settings.Smtp.Username))
        {
            await client.AuthenticateAsync(settings.Smtp.Username, settings.Smtp.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto,
    };
}
