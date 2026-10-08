using Kimlik.Application.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Infrastructure.Persistence;

public static class DatabasePreparationExtensions
{
    /// <summary>
    /// Applies pending migrations, brings the system catalog (permissions, roles and scopes Kimlik ships with)
    /// up to date and bootstraps the first administrator. EF Core holds a database lock while migrating, and an
    /// advisory lock covers the rest, so instances that start at the same time take turns.
    /// </summary>
    public static async Task PrepareDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<KimlikDbContext>();
        await context.Database.MigrateAsync(cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKeys.DatabasePreparation})", cancellationToken);

        await scope.ServiceProvider.GetRequiredService<SystemCatalog>().SyncAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<BootstrapAdministratorHandler>().HandleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
