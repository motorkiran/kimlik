using Microsoft.Extensions.Configuration;

namespace Kimlik.Infrastructure.Settings;

/// <summary>
/// The settings stored in the database as a configuration provider, and its own source. It starts empty, since the
/// database may not exist yet when the configuration is built, and <see cref="StoredSettings"/> fills it once it does.
/// </summary>
public sealed class StoredSettingsProvider : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    /// <summary>Replaces the stored settings, and lets the options that read them know.</summary>
    internal void Replace(IReadOnlyDictionary<string, string> values)
    {
        Data = values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}
