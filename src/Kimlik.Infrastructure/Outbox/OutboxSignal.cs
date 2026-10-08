using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Kimlik.Infrastructure.Outbox;

/// <summary>Wakes the dispatcher of this instance when new messages become visible, so it need not wait for the next poll.</summary>
internal sealed class OutboxSignal : IDisposable
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

/// <summary>Wakes the dispatcher once newly saved outbox messages are visible to it.</summary>
internal sealed class OutboxSignalInterceptor(OutboxSignal signal) : CommittedChangeInterceptor
{
    protected override bool HasChangesOfInterest(ChangeTracker changeTracker) =>
        changeTracker.Entries<OutboxMessage>().Any(entry => entry.State == EntityState.Added);

    protected override void OnVisible() => signal.Notify();
}
