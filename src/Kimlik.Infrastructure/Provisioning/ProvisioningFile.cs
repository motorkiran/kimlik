using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Kimlik.Contracts.Management;
using Microsoft.Extensions.Configuration;

namespace Kimlik.Infrastructure.Provisioning;

/// <summary>Reads a provisioning document written by people: comments are allowed, unknown properties are not.</summary>
internal static partial class ProvisioningFile
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>The JSON Schema of the document, which editors use for completion and validation.</summary>
    public static JsonNode Schema()
    {
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(
            SerializerOptions, typeof(ProvisioningDocument), new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });

        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "Kimlik provisioning document",
        };

        foreach (var (name, value) in schema.AsObject().ToList())
        {
            root[name] = value?.DeepClone();
        }

        return root;
    }

    public static async Task<ProvisioningDocument> ReadAsync(string path, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<ProvisioningDocument>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException($"The provisioning file '{path}' is empty.");

        return document with
        {
            Clients = document.Clients?.Select(client => client with { ClientSecret = Resolve(client.ClientSecret, configuration) }).ToList(),
        };
    }

    /// <summary>Secrets stay out of the file: <c>${Some:Setting}</c> reads one from configuration.</summary>
    private static string? Resolve(string? secret, IConfiguration configuration)
    {
        if (secret is null || SettingReference().Match(secret) is not { Success: true } reference)
        {
            return secret;
        }

        var key = reference.Groups["key"].Value;
        return configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"The provisioning file refers to the setting '{key}', which is not set.");
    }

    [GeneratedRegex(@"^\$\{(?<key>[^}]+)\}$", RegexOptions.CultureInvariant)]
    private static partial Regex SettingReference();
}
