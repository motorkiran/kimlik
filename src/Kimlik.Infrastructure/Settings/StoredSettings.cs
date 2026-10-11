using System.Security.Cryptography;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Settings;
using Kimlik.Contracts.Management;
using Kimlik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Npgsql;

namespace Kimlik.Infrastructure.Settings;

/// <summary>
/// Loads the settings stored in the database into <see cref="StoredSettingsProvider"/>: after the database is migrated,
/// at once after this instance changes them, and when <see cref="StoredSettingsRefresher"/> finds that another instance
/// did. Stored values for settings that are no longer in the catalog are left out.
/// </summary>
internal sealed partial class StoredSettings(
    StoredSettingsProvider provider, IConfiguration configuration, IServiceScopeFactory scopes, ILogger<StoredSettings> logger) : IStoredSettings, IDisposable
{
    private const string UndefinedTable = "42P01";

    private readonly SemaphoreSlim _loading = new(1, 1);
    private (int Count, DateTimeOffset? LastChange) _version;

    public SettingSource SourceOf(string key)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return SettingSource.Default;
        }

        var providers = root.Providers.ToList();
        var stored = providers.IndexOf(provider);
        for (var index = providers.Count - 1; index >= 0; index--)
        {
            if (providers[index].TryGet(key, out _))
            {
                return stored < 0 || index < stored ? SettingSource.Configuration
                    : index == stored ? SettingSource.Database
                    : SettingSource.Environment;
            }
        }

        return SettingSource.Default;
    }

    public IConfiguration WithStored(IReadOnlyDictionary<string, string> stored)
    {
        var candidate = new MemoryConfigurationProvider(new MemoryConfigurationSource());
        foreach (var (key, value) in stored)
        {
            candidate.Set(key, value);
        }

        var providers = configuration is IConfigurationRoot root
            ? root.Providers.Select(existing => ReferenceEquals(existing, provider) ? candidate : (IConfigurationProvider)new Unchanging(existing)).ToList()
            : [candidate];
        return new ConfigurationRoot(providers);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _loading.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<KimlikDbContext>();
            var encryption = scope.ServiceProvider.GetRequiredService<ISecretEncryption>();
            var rows = await context.Settings.AsNoTracking().ToListAsync(cancellationToken);

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.Where(row => SettingsCatalog.Find(row.Key) is not null))
            {
                try
                {
                    values[row.Key] = StoredSettingSecrets.Decrypt(encryption, row);
                }
                catch (CryptographicException exception)
                {
                    LogUnreadable(logger, row.Key, exception);
                }
            }

            // Values the validators reject, such as ones an earlier version accepted, would keep their options from binding:
            // their section goes back to the configuration alone.
            foreach (var (section, failures) in SettingsCatalog.Validate(WithStored(values), values.Keys, scope.ServiceProvider))
            {
                LogRejected(logger, section, string.Join(" ", failures));
                foreach (var key in values.Keys.Where(key => key.StartsWith($"{section}:", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    values.Remove(key);
                }
            }

            provider.Replace(values);
            _version = (rows.Count, rows.Count == 0 ? null : rows.Max(row => row.UpdatedAt));
        }
        catch (PostgresException exception) when (exception.SqlState == UndefinedTable)
        {
            // The database is not migrated yet: the configuration alone applies.
            LogNotMigrated(logger);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Kimlik starts while the database is unreachable, and reports itself not ready; the refresher tries again.
            LogUnavailable(logger, exception);
        }
        finally
        {
            _loading.Release();
        }
    }

    /// <summary>Loads the settings again when they changed since they were loaded, such as on another instance.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        (int, DateTimeOffset?) version;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<KimlikDbContext>().Settings.AsNoTracking();
            version = (await settings.CountAsync(cancellationToken), await settings.MaxAsync(setting => (DateTimeOffset?)setting.UpdatedAt, cancellationToken));
        }

        if (version != _version)
        {
            await ReloadAsync(cancellationToken);
        }
    }

    public void Dispose() => _loading.Dispose();

    [LoggerMessage(LogLevel.Warning, "The stored setting {Key} could not be decrypted and is left out; set it again")]
    private static partial void LogUnreadable(ILogger logger, string key, Exception exception);

    [LoggerMessage(LogLevel.Warning, "The stored settings of {Section} are left out, as they are not valid: {Failures} Set them again")]
    private static partial void LogRejected(ILogger logger, string section, string failures);

    [LoggerMessage(LogLevel.Warning, "The stored settings could not be loaded, so the configuration alone applies until they can")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "The database has no settings table yet, so stored settings do not apply until it is migrated")]
    private static partial void LogNotMigrated(ILogger logger);

    /// <summary>A provider seen as it is now, for a configuration that only validates a change.</summary>
    private sealed class Unchanging(IConfigurationProvider inner) : IConfigurationProvider
    {
        public bool TryGet(string key, out string? value) => inner.TryGet(key, out value);

        public void Set(string key, string? value)
        {
        }

        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

        public void Load()
        {
        }

        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath) => inner.GetChildKeys(earlierKeys, parentPath);
    }
}

/// <summary>Picks up settings that another instance changed, every 15 seconds.</summary>
internal sealed partial class StoredSettingsRefresher(StoredSettings settings, TimeProvider timeProvider, ILogger<StoredSettingsRefresher> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, timeProvider, stoppingToken);
                await settings.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(LogLevel.Warning, "The stored settings could not be checked for changes")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
