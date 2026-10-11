using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kimlik.Infrastructure.Settings;

public static class StoredSettingsExtensions
{
    /// <summary>
    /// Adds the settings stored in the database to the configuration, after the configuration files and before the
    /// environment variables and the command line: stored values override the files, and the environment fixes values.
    /// </summary>
    public static IHostApplicationBuilder AddStoredSettings(this IHostApplicationBuilder builder)
    {
        var provider = new StoredSettingsProvider();
        var sources = builder.Configuration.Sources;
        var lastFile = sources.Select((source, index) => (source, index)).LastOrDefault(entry => entry.source is FileConfigurationSource);
        sources.Insert(lastFile.source is null ? 0 : lastFile.index + 1, provider);
        builder.Services.AddSingleton(provider);
        return builder;
    }

    /// <summary>Loads the stored settings, once the database is migrated.</summary>
    public static Task LoadStoredSettingsAsync(this IServiceProvider services, CancellationToken cancellationToken = default) =>
        services.GetRequiredService<StoredSettings>().ReloadAsync(cancellationToken);
}
