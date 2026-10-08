using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using OpenIddict.EntityFrameworkCore.Models;

namespace Kimlik.Infrastructure.Clients;

/// <summary>
/// Counts committed changes to clients in this instance, so caches of client settings can tell that they are
/// stale. Other instances notice changes when their caches expire.
/// </summary>
public sealed class ClientChangeSignal
{
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    internal void Notify() => Interlocked.Increment(ref _version);
}

internal sealed class ClientChangeInterceptor(ClientChangeSignal signal) : CommittedChangeInterceptor
{
    protected override bool HasChangesOfInterest(ChangeTracker changeTracker) =>
        changeTracker.Entries<OpenIddictEntityFrameworkCoreApplication<Guid>>()
            .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

    protected override void OnVisible() => signal.Notify();
}
