using Kimlik.Application.Abstractions;

namespace Kimlik.Infrastructure.Webhooks;

/// <summary>Wakes the sender of this instance when deliveries are waiting.</summary>
internal sealed class WebhookSignal : IWebhookSignal, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signaled and not yet observed.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _signal.Dispose();
}
