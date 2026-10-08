using System.Text;
using System.Text.Json.Nodes;
using Kimlik.Domain.Common;

namespace Kimlik.Application.Common;

/// <summary>Metadata: JSON objects that applications keep about users and organizations, of up to 8 KB each.</summary>
internal static class Metadata
{
    public const int MaxBytes = 8 * 1024;

    public static readonly Error TooLarge = Error.Validation("metadata.too_large", "Metadata can take up to 8 KB.");

    /// <summary>The JSON to store; <see langword="null"/> clears the metadata.</summary>
    public static Result<string> Serialize(JsonObject? value)
    {
        var json = value?.ToJsonString() ?? "{}";
        return Encoding.UTF8.GetByteCount(json) <= MaxBytes ? json : TooLarge;
    }

    public static JsonObject Parse(string json) => JsonNode.Parse(json)?.AsObject() ?? [];
}
