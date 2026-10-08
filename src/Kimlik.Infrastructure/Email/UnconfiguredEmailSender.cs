using Kimlik.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kimlik.Infrastructure.Email;

/// <summary>
/// Used when no SMTP relay is configured. Delivery fails with a clear error instead of dropping the message
/// silently; the outbox keeps retrying it, so messages queued shortly before email is configured still go out.
/// </summary>
internal sealed partial class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogNotConfigured(logger, message.Subject);
        throw new InvalidOperationException("Email delivery is not configured. Set Kimlik:Email:FromAddress and Kimlik:Email:Smtp:Host.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email \"{Subject}\" was not sent because email delivery is not configured")]
    private static partial void LogNotConfigured(ILogger logger, string subject);
}
