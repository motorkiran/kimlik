using Kimlik.Contracts.Management;
using Microsoft.Extensions.Configuration;

namespace Kimlik.Application.Abstractions;

/// <summary>The product settings stored in the database, as a layer of the configuration.</summary>
public interface IStoredSettings
{
    /// <summary>Where the value of the configuration path comes from now.</summary>
    SettingSource SourceOf(string key);

    /// <summary>The configuration as it would be with the stored settings replaced by <paramref name="stored"/>, to validate a change.</summary>
    IConfiguration WithStored(IReadOnlyDictionary<string, string> stored);

    /// <summary>Reads the stored settings again, so that this instance applies a change at once.</summary>
    Task ReloadAsync(CancellationToken cancellationToken);
}
