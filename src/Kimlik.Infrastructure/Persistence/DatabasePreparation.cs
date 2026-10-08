using Kimlik.Application.Bootstrap;
using Kimlik.Application.Provisioning;
using Kimlik.Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Persistence;

/// <summary>
/// Brings the database up to date: migrations, the system catalog, the first administrator and the provisioning
/// file. EF Core holds a database lock while migrating, and an advisory lock covers the rest, so instances that
/// start at the same time take turns. Any failure stops startup: it means the configuration is wrong.
/// </summary>
internal sealed partial class DatabasePreparation(
    KimlikDbContext context,
    SystemCatalog catalog,
    BootstrapAdministratorHandler bootstrap,
    ApplyProvisioningHandler provisioning,
    IOptions<ProvisioningOptions> provisioningOptions,
    IConfiguration configuration,
    ILogger<DatabasePreparation> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await context.Database.MigrateAsync(cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKeys.DatabasePreparation})", cancellationToken);

        await catalog.SyncAsync(cancellationToken);

        // Before provisioning, which may make a client an administrator: a new installation still gets its first person.
        await bootstrap.HandleAsync(cancellationToken);

        if (provisioningOptions.Value.FilePath is { Length: > 0 } path)
        {
            await ApplyProvisioningFileAsync(path, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ApplyProvisioningFileAsync(string path, CancellationToken cancellationToken)
    {
        var document = await ProvisioningFile.ReadAsync(path, configuration, cancellationToken);
        var result = await provisioning.HandleAsync(document, cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"The provisioning file '{path}' cannot be applied. {result.Error.Code}: {result.Error.Message}");
        }

        LogProvisioningApplied(logger, path, result.Value.Created, result.Value.Updated, result.Value.Unchanged);
    }

    [LoggerMessage(LogLevel.Information, "Applied the provisioning file {Path}: {Created} created, {Updated} updated, {Unchanged} unchanged")]
    private static partial void LogProvisioningApplied(ILogger logger, string path, int created, int updated, int unchanged);
}
