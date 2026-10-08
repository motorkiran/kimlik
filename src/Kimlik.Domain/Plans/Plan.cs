using Kimlik.Domain.Common;

namespace Kimlik.Domain.Plans;

/// <summary>The value a plan sets for a feature. <see cref="Limit"/> is the maximum of a limit; null means unlimited.</summary>
public readonly record struct FeatureSetting(Guid FeatureId, bool Enabled, long? Limit);

/// <summary>
/// A named set of feature values, such as Free or Pro. A feature the plan does not set is off, or a limit of zero.
/// Archived plans keep their subscribers but take no new ones.
/// </summary>
public sealed class Plan
{
    private readonly List<PlanFeature> _features = [];

    // Used by EF Core.
    private Plan()
    {
    }

    /// <summary>No plan at all: every feature is off, or zero.</summary>
    public static Plan None { get; } = new();

    public Guid Id { get; private init; }

    public string Key { get; private init; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<PlanFeature> Features => _features;

    public static Result<Plan> Create(string key, string name, string? description, DateTimeOffset now)
    {
        if (!Feature.IsValidKey(key))
        {
            return PlanErrors.InvalidPlanKey;
        }

        var validation = Feature.Validate(name, description);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new Plan
        {
            Id = Guid.CreateVersion7(now),
            Key = key,
            Name = name.Trim(),
            Description = Feature.Normalize(description),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public Result Update(string name, string? description, bool isArchived, DateTimeOffset now)
    {
        var validation = Feature.Validate(name, description);
        if (validation.IsFailure)
        {
            return validation;
        }

        Name = name.Trim();
        Description = Feature.Normalize(description);
        IsArchived = isArchived;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Replaces the feature values of the plan.</summary>
    public Result SetFeatures(IEnumerable<FeatureSetting> settings, DateTimeOffset now)
    {
        var wanted = settings.ToList();
        if (wanted.Exists(setting => setting.Limit is < 0))
        {
            return PlanErrors.InvalidFeatureValue;
        }

        if (wanted.DistinctBy(setting => setting.FeatureId).Count() != wanted.Count)
        {
            return PlanErrors.InvalidFeatureValue;
        }

        _features.Clear();
        _features.AddRange(wanted.Select(setting => new PlanFeature(Id, setting.FeatureId, setting.Enabled, setting.Limit)));
        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>A feature value of a plan.</summary>
public sealed class PlanFeature(Guid planId, Guid featureId, bool enabled, long? limit)
{
    public Guid PlanId { get; private init; } = planId;

    public Guid FeatureId { get; private init; } = featureId;

    /// <summary>Whether a boolean feature is on. Limits are always enabled.</summary>
    public bool Enabled { get; private init; } = enabled;

    /// <summary>The maximum of a limit; null means unlimited. Unused for boolean features.</summary>
    public long? Limit { get; private init; } = limit;
}
