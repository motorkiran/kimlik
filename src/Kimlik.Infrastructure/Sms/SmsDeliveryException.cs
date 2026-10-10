namespace Kimlik.Infrastructure.Sms;

/// <summary>The provider did not accept a text; the outbox tries again later. The message carries no credentials.</summary>
public sealed class SmsDeliveryException(string provider, string reason) : Exception($"{provider} did not accept the text message: {reason}")
{
    public string Provider { get; } = provider;
}
