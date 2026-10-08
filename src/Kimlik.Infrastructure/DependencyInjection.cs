using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Infrastructure;

public static class DependencyInjection
{
    private const string MigrationsHistoryTable = "__ef_migrations_history";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        AddPersistence(services);
        return services;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .Validate<IConfiguration>(
                (_, configuration) => !string.IsNullOrWhiteSpace(GetConnectionString(configuration)),
                $"The connection string '{DatabaseOptions.ConnectionStringName}' is not configured.")
            .ValidateOnStart();

        // The connection string is resolved when a context is created, so configuration
        // added late (integration tests, design-time tools) is honored.
        services.AddDbContext<KimlikDbContext>((serviceProvider, options) => options
            .UseNpgsql(
                GetConnectionString(serviceProvider.GetRequiredService<IConfiguration>()),
                npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, KimlikDbContext.Schema))
            .UseSnakeCaseNamingConvention());
    }

    private static string? GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
}
