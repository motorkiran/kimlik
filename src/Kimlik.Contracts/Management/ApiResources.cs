using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

/// <summary>
/// An API protected by Kimlik tokens. Clients request its <c>scope</c>; the access tokens they get name its
/// <c>audience</c>, which the API checks. The system resource <c>kimlik</c> is Kimlik's own API.
/// </summary>
public sealed record ApiResourceResponse(Guid Id, string Scope, string Audience, string? DisplayName, string? Description, bool IsSystem);

public sealed record CreateApiResourceRequest
{
    /// <summary>
    /// The scope clients request, such as <c>orders</c>: lowercase letters, digits, <c>.</c>, <c>:</c>, <c>_</c> and
    /// <c>-</c>, starting with a letter. It cannot be changed later.
    /// </summary>
    [Required]
    [StringLength(100)]
    public required string Scope { get; init; }

    /// <summary>The <c>aud</c> of the access tokens, such as <c>orders-api</c> or a URL; the scope when omitted. It cannot be changed later.</summary>
    [StringLength(200)]
    public string? Audience { get; init; }

    /// <summary>Shown to users on the consent screen, such as "Manage your orders".</summary>
    [StringLength(100)]
    public string? DisplayName { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }
}

/// <summary>Changes an API resource with JSON Merge Patch semantics: an omitted property keeps its value and <c>null</c> clears it.</summary>
public sealed record UpdateApiResourceRequest
{
    [StringLength(100)]
    public string? DisplayName
    {
        get;
        init
        {
            field = value;
            HasDisplayName = true;
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

    /// <summary>Whether the request sets <see cref="DisplayName"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasDisplayName { get; private init; }

    /// <summary>Whether the request sets <see cref="Description"/>, possibly to <see langword="null"/>.</summary>
    [JsonIgnore]
    public bool HasDescription { get; private init; }
}
