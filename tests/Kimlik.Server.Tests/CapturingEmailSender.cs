using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Kimlik.Application.Abstractions;

namespace Kimlik.Server.Tests;

/// <summary>Collects the emails Kimlik sends, so tests can read them and follow their links.</summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public IEnumerable<EmailMessage> SentTo(string address) =>
        _messages.Where(message => string.Equals(message.To, address, StringComparison.OrdinalIgnoreCase));

    /// <summary>Waits for the outbox to deliver an email to <paramref name="address"/> whose subject contains <paramref name="subject"/>.</summary>
    public async Task<EmailMessage> WaitForAsync(string address, string subject)
    {
        using var timeout = new CancellationTokenSource(Timeout);

        while (true)
        {
            if (SentTo(address).FirstOrDefault(message => message.Subject.Contains(subject, StringComparison.Ordinal)) is { } message)
            {
                return message;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }
    }

    /// <summary>The path and query of the first link to Kimlik in the plain text part of an email.</summary>
    public static string LinkIn(EmailMessage message) =>
        new Uri(LinkPattern().Match(message.TextBody) is { Success: true } match
            ? match.Value
            : throw new InvalidOperationException($"No link in \"{message.Subject}\".")).PathAndQuery;

    [GeneratedRegex(@"https?://localhost/\S+")]
    private static partial Regex LinkPattern();
}
