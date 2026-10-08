using System.Text.Json;
using Kimlik.Domain.Common;
using Kimlik.Domain.Plans;

namespace Kimlik.Application.Plans;

/// <summary>Reads and writes plan feature values in their JSON form: booleans, and numbers or null for limits.</summary>
internal static class FeatureValues
{
    private static readonly JsonElement True = JsonSerializer.SerializeToElement(true);
    private static readonly JsonElement False = JsonSerializer.SerializeToElement(false);
    private static readonly JsonElement Unlimited = JsonSerializer.SerializeToElement<long?>(null);

    public static Result<List<FeatureSetting>> Parse(IReadOnlyDictionary<string, JsonElement> values, IReadOnlyDictionary<string, Feature> features)
    {
        var settings = new List<FeatureSetting>(values.Count);
        foreach (var (key, value) in values)
        {
            if (!features.TryGetValue(key, out var feature))
            {
                return PlanErrors.UnknownFeature;
            }

            FeatureSetting? setting = (feature.Type, value.ValueKind) switch
            {
                (FeatureType.Boolean, JsonValueKind.True) => new FeatureSetting(feature.Id, Enabled: true, Limit: null),
                (FeatureType.Boolean, JsonValueKind.False) => new FeatureSetting(feature.Id, Enabled: false, Limit: null),
                (FeatureType.Limit, JsonValueKind.Null) => new FeatureSetting(feature.Id, Enabled: true, Limit: null),
                (FeatureType.Limit, JsonValueKind.Number) when value.TryGetInt64(out var limit) && limit >= 0 => new FeatureSetting(feature.Id, Enabled: true, limit),
                _ => null,
            };

            if (setting is null)
            {
                return PlanErrors.InvalidFeatureValue;
            }

            settings.Add(setting.Value);
        }

        return settings;
    }

    /// <summary>Whether the plan already gives what the settings would, counting unset features as off, or zero.</summary>
    public static bool HasSameEffect(Plan plan, IReadOnlyList<FeatureSetting> settings, IReadOnlyCollection<Feature> features)
    {
        var current = plan.Features.ToDictionary(value => value.FeatureId, value => Effective(features, value.FeatureId, value.Enabled, value.Limit));
        var wanted = settings.ToDictionary(setting => setting.FeatureId, setting => Effective(features, setting.FeatureId, setting.Enabled, setting.Limit));

        return features.All(feature =>
            current.GetValueOrDefault(feature.Id, Off(feature)) == wanted.GetValueOrDefault(feature.Id, Off(feature)));
    }

    /// <summary>The value of every feature in the plan, by key; features the plan does not set are off, or zero.</summary>
    public static Dictionary<string, JsonElement> Describe(Plan plan, IEnumerable<Feature> features)
    {
        var values = plan.Features.ToDictionary(value => value.FeatureId);
        return features.OrderBy(feature => feature.Key, StringComparer.Ordinal).ToDictionary(
            feature => feature.Key,
            feature => (feature.Type, values.GetValueOrDefault(feature.Id)) switch
            {
                (FeatureType.Boolean, { Enabled: true }) => True,
                (FeatureType.Boolean, _) => False,
                (FeatureType.Limit, null) => JsonSerializer.SerializeToElement(0L),
                (FeatureType.Limit, { Limit: null }) => Unlimited,
                (FeatureType.Limit, { Limit: { } limit }) => JsonSerializer.SerializeToElement(limit),
                _ => throw new InvalidOperationException($"Unknown feature type {feature.Type}."),
            },
            StringComparer.Ordinal);
    }

    private static (bool Enabled, long? Limit) Effective(IReadOnlyCollection<Feature> features, Guid featureId, bool enabled, long? limit) =>
        features.FirstOrDefault(feature => feature.Id == featureId)?.Type == FeatureType.Boolean ? (enabled, null) : (true, limit);

    private static (bool Enabled, long? Limit) Off(Feature feature) => feature.Type == FeatureType.Boolean ? (false, null) : (true, 0);
}
