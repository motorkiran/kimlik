namespace Kimlik.Application.Abstractions;

/// <summary>Wakes the webhook sender up when deliveries are waiting, instead of letting them wait for its next poll.</summary>
public interface IWebhookSignal
{
    void Notify();
}
