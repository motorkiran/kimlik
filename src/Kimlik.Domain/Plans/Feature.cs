using System.Text.RegularExpressions;
using Kimlik.Domain.Common;

namespace Kimlik.Domain.Plans;

public enum FeatureType
{
    /// <summary>On or off, such as <c>export_pdf</c>.</summary>
    Boolean,

    /// <summary>A maximum, such as <c>max_projects</c>; a plan may also make it unlimited.</summary>
    Limit,
}

/// <summary>An entitlement the application defines and plans set a value for.</summary>
public sealed partial class Feature
{
    public const int KeyMaxLength = 64;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 256;

    // Used by EF Core.
    private Feature()
    {
    }

    public Guid Id { get; private init; }

    public string Key { get; private init; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public FeatureType Type { get; private init; }

    /// <summary>Whether a limit counts use a month, such as API calls, rather than things, such as projects.</summary>
    public bool IsMetered { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Feature> Create(string key, string name, string? description, FeatureType type, DateTimeOffset now, bool metered = false)
    {
        if (metered && type != FeatureType.Limit)
        {
            return PlanErrors.MeteredLimitsOnly;
        }

        if (!IsValidKey(key))
        {
            return PlanErrors.InvalidFeatureKey;
        }

        var validation = Validate(name, description);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new Feature
        {
            Id = Guid.CreateVersion7(now),
            Key = key,
            Name = name.Trim(),
            Description = Normalize(description),
            Type = type,
            IsMetered = metered,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public Result Update(string name, string? description, DateTimeOffset now)
    {
        var validation = Validate(name, description);
        if (validation.IsFailure)
        {
            return validation;
        }

        Name = name.Trim();
        Description = Normalize(description);
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Lowercase letters, digits, <c>_</c> and <c>-</c>, starting with a letter; plan keys share the format.</summary>
    public static bool IsValidKey(string? key) => key is { Length: > 0 and <= KeyMaxLength } && KeyPattern().IsMatch(key);

    internal static Result Validate(string? name, string? description)
    {
        if (name is null || name.Trim().Length is 0 or > NameMaxLength)
        {
            return PlanErrors.InvalidName;
        }

        return Normalize(description) is { Length: > DescriptionMaxLength } ? PlanErrors.InvalidDescription : Result.Success();
    }

    internal static string? Normalize(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    [GeneratedRegex("^[a-z][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
