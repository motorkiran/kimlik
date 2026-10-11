using System.ComponentModel.DataAnnotations;

namespace Kimlik.Contracts.Management;

/// <summary>What kind of value a setting takes.</summary>
public enum SettingType
{
    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>A whole number.</summary>
    Number,

    Text,

    /// <summary>An absolute HTTP or HTTPS URL.</summary>
    Url,

    /// <summary>A color as <c>#rrggbb</c>.</summary>
    Color,

    /// <summary>A time span as <c>d.hh:mm:ss</c>, such as <c>7.00:00:00</c> for a week.</summary>
    Duration,

    /// <summary>One of <c>Choices</c>.</summary>
    Choice,

    /// <summary>The key of a plan.</summary>
    Plan,

    /// <summary>A secret, which is never returned.</summary>
    Secret,
}

/// <summary>Where a setting's value comes from.</summary>
public enum SettingSource
{
    /// <summary>Nothing sets it; Kimlik's default applies.</summary>
    Default,

    /// <summary>The configuration files, which a value changed at runtime overrides.</summary>
    Configuration,

    /// <summary>Changed at runtime, through the admin panel or the API.</summary>
    Database,

    /// <summary>Environment variables or the command line, which fix the value: it cannot change at runtime.</summary>
    Environment,
}

/// <summary>
/// A product setting that can change at runtime, under its configuration path, such as
/// <c>Kimlik:Accounts:Registration</c>. <c>Value</c> is the value in effect, and is never returned for secrets, whose
/// <c>IsSet</c> says whether they have one.
/// </summary>
public sealed record SettingResponse(
    string Key,
    string Group,
    SettingType Type,
    string Description,
    string? Value,
    bool IsSet,
    SettingSource Source,
    bool Editable,
    IReadOnlyList<string> Choices);

/// <summary>New values by configuration path; <see langword="null"/> returns a setting to its configured or default value.</summary>
public sealed record UpdateSettingsRequest
{
    [Required]
    public required IReadOnlyDictionary<string, string?> Values { get; init; }
}
