using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Kimlik.Client;

/// <summary>The JSON conventions of the Management API.</summary>
internal static class KimlikJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { WriteOnlySetProperties } },
    };

    /// <summary>
    /// Update requests use JSON Merge Patch, where an omitted property is left alone and <c>null</c> clears it. Their
    /// properties with a matching <c>Has…</c> flag are written only when the request set them.
    /// </summary>
    private static void WriteOnlySetProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.AttributeProvider is PropertyInfo { Name: var name }
                && typeInfo.Type.GetProperty($"Has{name}") is { PropertyType: var flagType } flag
                && flagType == typeof(bool))
            {
                property.ShouldSerialize = (request, _) => (bool)flag.GetValue(request)!;
            }
        }
    }
}
