using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

public enum FeatureType
{
    /// <summary>On or off, such as <c>export_pdf</c>.</summary>
    Boolean,

    /// <summary>A maximum, such as <c>max_projects</c>; a plan may also make it unlimited.</summary>
    Limit,
}

/// <summary>An entitlement the application defines; plans set a value for it.</summary>
public sealed record FeatureResponse(Guid Id, string Key, string Name, string? Description, FeatureType Type, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateFeatureRequest
{
    /// <summary>Lowercase letters, digits, <c>_</c> and <c>-</c>, such as <c>max_projects</c>. It cannot be changed later.</summary>
    [Required]
    [StringLength(64)]
    public required string Key { get; init; }

    [Required]
    [StringLength(100)]
    public required string Name { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }

    /// <summary>It cannot be changed later.</summary>
    public required FeatureType Type { get; init; }
}

/// <summary>Changes a feature with JSON Merge Patch semantics: an omitted property keeps its value and <c>null</c> clears it.</summary>
public sealed record UpdateFeatureRequest
{
    [StringLength(100)]
    public string? Name
    {
        get;
        init
        {
            field = value;
            HasName = true;
        }
    }

    [StringLength(256)]
    public string? Description
    {
        get;
        init
        {
            field = value;
            HasDescription = true;
        }
    }

    /// <summary>Whether the request sets <see cref="Name"/>.</summary>
    [JsonIgnore]
    public bool HasName { get; private init; }

    /// <summary>Whether the request sets <see cref="Description"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasDescription { get; private init; }
}

/// <summary>
/// A plan and the value of every feature in it: <c>true</c> or <c>false</c> for boolean features, and a maximum
/// for limits, where <c>null</c> means unlimited.
/// </summary>
public sealed record PlanResponse(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    bool IsArchived,
    IReadOnlyDictionary<string, JsonElement> Features,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreatePlanRequest
{
    /// <summary>Lowercase letters, digits, <c>_</c> and <c>-</c>, such as <c>pro</c>. It cannot be changed later.</summary>
    [Required]
    [StringLength(64)]
    public required string Key { get; init; }

    [Required]
    [StringLength(100)]
    public required string Name { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }

    /// <summary>Feature values by key, such as <c>{ "export_pdf": true, "max_projects": 3 }</c>. Features left out are off, or zero.</summary>
    public IReadOnlyDictionary<string, JsonElement> Features { get; init; } = new Dictionary<string, JsonElement>();
}

/// <summary>Changes a plan with JSON Merge Patch semantics: an omitted property keeps its value; <c>features</c> replaces them all.</summary>
public sealed record UpdatePlanRequest
{
    [StringLength(100)]
    public string? Name
    {
        get;
        init
        {
            field = value;
            HasName = true;
        }
    }

    [StringLength(256)]
    public string? Description
    {
        get;
        init
        {
            field = value;
            HasDescription = true;
        }
    }

    /// <summary>Archived plans keep their subscribers but take no new ones.</summary>
    public bool? IsArchived { get; init; }

    public IReadOnlyDictionary<string, JsonElement>? Features { get; init; }

    /// <summary>Whether the request sets <see cref="Name"/>.</summary>
    [JsonIgnore]
    public bool HasName { get; private init; }

    /// <summary>Whether the request sets <see cref="Description"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasDescription { get; private init; }
}
