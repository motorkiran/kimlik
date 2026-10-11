namespace Kimlik.Domain.Settings;

/// <summary>
/// A product setting changed at runtime, kept as the configuration value it overrides, under its configuration path,
/// such as <c>Kimlik:Accounts:Registration</c>.
/// </summary>
public sealed class Setting
{
    public const int KeyMaxLength = 200;

    // Used by EF Core.
    private Setting()
    {
    }

    public string Key { get; private init; } = string.Empty;

    /// <summary>The value as configuration reads it; a secret's is encrypted with the master key.</summary>
    public string Value { get; private set; } = string.Empty;

    public bool IsSecret { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Setting Create(string key, string value, bool isSecret, DateTimeOffset now) =>
        new() { Key = key, Value = value, IsSecret = isSecret, UpdatedAt = now };

    public void Change(string value, DateTimeOffset now)
    {
        Value = value;
        UpdatedAt = now;
    }
}
