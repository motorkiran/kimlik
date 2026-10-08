using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Infrastructure.Persistence;

public static class DatabasePreparationExtensions
{
    /// <summary>
    /// Applies pending migrations, then brings the system catalog (permissions, roles and scopes Kimlik ships
    /// with) up to date. EF Core holds a database lock while migrating, so instances that start at the same
    /// time do not apply migrations concurrently.
    /// </summary>
    public static async Task PrepareDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KimlikDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<SystemCatalog>().SyncAsync(cancellationToken);
    }
}
