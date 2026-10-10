using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Kimlik.Application.Abstractions;

namespace Kimlik.Server.Tests;

/// <summary>Collects the text messages Kimlik sends, so tests can read their codes.</summary>
public sealed partial class CapturingSmsSender : ISmsSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentQueue<SmsMessage> _messages = new();

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public IEnumerable<SmsMessage> SentTo(string phoneNumber) => _messages.Where(message => message.To == phoneNumber);

    /// <summary>Waits for the outbox to deliver a text to <paramref name="phoneNumber"/>, and returns the code in it.</summary>
    public async Task<string> WaitForCodeAsync(string phoneNumber, int count = 1)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (true)
        {
            if (SentTo(phoneNumber).Skip(count - 1).FirstOrDefault() is { } message)
            {
                return Code().Match(message.Text).Value;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }
    }

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex Code();
}
