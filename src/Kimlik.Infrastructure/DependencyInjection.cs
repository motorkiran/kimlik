using Kimlik.Infrastructure.Persistence;
using Kimlik.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure;

public static class DependencyInjection
{
    private const string MigrationsHistoryTable = "__ef_migrations_history";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        AddPersistence(services);
        AddSecurity(services);
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

    private static void AddSecurity(IServiceCollection services)
    {
        services.AddOptions<SecurityOptions>()
            .BindConfiguration(SecurityOptions.SectionName)
            .Validate(
                options => SecurityOptions.IsValidMasterKey(options.MasterKey),
                $"{SecurityOptions.SectionName}:MasterKey must be a base64-encoded 256-bit key. Generate one with 'openssl rand -base64 32'.")
            .ValidateOnStart();

        services.AddSingleton<ISecretProtector, SecretProtector>();

        // Every instance shares one key ring, stored in PostgreSQL and encrypted with the master key.
        services.AddDataProtection()
            .SetApplicationName("Kimlik")
            .PersistKeysToDbContext<KimlikDbContext>();
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>, ConfigureKeyRingEncryption>();
    }

    private static string? GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
}
