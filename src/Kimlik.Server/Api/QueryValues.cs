using System.Text.Json;

namespace Kimlik.Server.Api;

/// <summary>
/// Parses query string values the way their JSON form is written. Minimal APIs would parse enums by their C#
/// names, accept numbers, and quietly ignore a value they cannot parse.
/// </summary>
internal static class QueryValues
{
    /// <summary>Accepts the camelCase name of a defined value, or no value at all.</summary>
    public static bool TryParseEnum<TEnum>(string? value, out TEnum? result)
        where TEnum : struct, Enum
    {
        result = null;
        if (value is null)
        {
            return true;
        }

        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(JsonNamingPolicy.CamelCase.ConvertName(candidate.ToString()), value, StringComparison.Ordinal))
            {
                result = candidate;
                return true;
            }
        }

        return false;
    }
}
