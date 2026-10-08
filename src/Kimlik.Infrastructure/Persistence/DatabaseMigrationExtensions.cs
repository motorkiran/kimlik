using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Infrastructure.Persistence;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Applies pending migrations. EF Core holds a database lock while migrating, so instances
    /// that start at the same time do not apply migrations concurrently.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<KimlikDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
