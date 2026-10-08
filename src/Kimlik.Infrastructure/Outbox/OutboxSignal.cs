using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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

/// <summary>
/// Notifies the dispatcher once saved outbox messages are visible to other connections: right after the save,
/// or after the commit when the save ran inside an explicit transaction. One instance per context.
/// </summary>
internal sealed class OutboxSignalInterceptor(OutboxSignal signal) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private bool _savingMessages;
    private bool _awaitingCommit;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        OnSaving(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        OnSaving(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        OnSaved();
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        OnSaved();
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => OnCommitted();

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        OnCommitted();
        return Task.CompletedTask;
    }

    private void OnSaving(DbContext? context)
    {
        if (context is null || !context.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.State == EntityState.Added))
        {
            return;
        }

        if (context.Database.CurrentTransaction is null)
        {
            _savingMessages = true;
        }
        else
        {
            _awaitingCommit = true;
        }
    }

    private void OnSaved()
    {
        if (_savingMessages)
        {
            _savingMessages = false;
            signal.Notify();
        }
    }

    private void OnCommitted()
    {
        if (_awaitingCommit)
        {
            _awaitingCommit = false;
            signal.Notify();
        }
    }
}
