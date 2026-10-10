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

    /// <summary>
    /// The value of every feature for a subscriber, by key: the plan's, then each add-on's for its quantity, then the
    /// subscription's overrides. An add-on turns a boolean feature on, and raises a limit by its value for each unit, or
    /// makes it unlimited.
    /// </summary>
    public static Dictionary<string, JsonElement> Describe(
        Plan plan, IReadOnlyCollection<(Plan AddOn, int Quantity)> addOns, IReadOnlyCollection<SubscriptionFeatureOverride> overrides, IEnumerable<Feature> features)
    {
        var values = Describe(plan, features);
        foreach (var feature in features)
        {
            var value = values[feature.Key];
            foreach (var (addOn, quantity) in addOns)
            {
                if (addOn.Features.FirstOrDefault(setting => setting.FeatureId == feature.Id) is not { } setting)
                {
                    continue;
                }

                value = (feature.Type, value.ValueKind) switch
                {
                    (FeatureType.Boolean, _) when setting.Enabled => True,
                    (FeatureType.Limit, JsonValueKind.Number) when setting.Limit is null => Unlimited,
                    (FeatureType.Limit, JsonValueKind.Number) => JsonSerializer.SerializeToElement(Add(value.GetInt64(), setting.Limit.Value, quantity)),
                    _ => value,
                };
            }

            if (overrides.FirstOrDefault(setting => setting.FeatureId == feature.Id) is { } overridden)
            {
                value = ValueOf(feature.Type, overridden.Enabled, overridden.Limit);
            }

            values[feature.Key] = value;
        }

        return values;
    }

    /// <summary>The values of feature settings by key, as an API returns them.</summary>
    public static Dictionary<string, JsonElement> Describe(IEnumerable<SubscriptionFeatureOverride> overrides, IEnumerable<Feature> features)
    {
        var byId = features.ToDictionary(feature => feature.Id);
        return overrides.Where(setting => byId.ContainsKey(setting.FeatureId)).ToDictionary(
            setting => byId[setting.FeatureId].Key,
            setting => ValueOf(byId[setting.FeatureId].Type, setting.Enabled, setting.Limit),
            StringComparer.Ordinal);
    }

    private static JsonElement ValueOf(FeatureType type, bool enabled, long? limit) =>
        type == FeatureType.Boolean ? (enabled ? True : False) : limit is { } value ? JsonSerializer.SerializeToElement(value) : Unlimited;

    /// <summary>A limit raised by an add-on, which stays within what a limit can hold.</summary>
    private static long Add(long limit, long increment, int quantity)
    {
        try
        {
            return checked(limit + (increment * quantity));
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static (bool Enabled, long? Limit) Effective(IReadOnlyCollection<Feature> features, Guid featureId, bool enabled, long? limit) =>
        features.FirstOrDefault(feature => feature.Id == featureId)?.Type == FeatureType.Boolean ? (enabled, null) : (true, limit);

    private static (bool Enabled, long? Limit) Off(Feature feature) => feature.Type == FeatureType.Boolean ? (false, null) : (true, 0);
}
