namespace Kimlik.Application.Abstractions;

/// <summary>A text message to a phone number in E.164 form, such as <c>+905321234567</c>.</summary>
public sealed record SmsMessage(string To, string Text);

/// <summary>Sends text messages through the provider that <c>Kimlik:Sms</c> configures.</summary>
public interface ISmsSender
{
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken);
}
