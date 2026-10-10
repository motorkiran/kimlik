using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

/// <summary>
/// The access model of an installation as one document: permissions, roles, features, plans, API resources and clients.
/// Applying it creates what is missing and updates what differs, and never deletes. Each item is described in
/// full, except that role permissions, client roles and client secrets are left alone when omitted.
/// </summary>
public sealed record ProvisioningDocument
{
    /// <summary>The JSON Schema of the document, for editor completion; ignored by Kimlik.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public IReadOnlyList<ProvisionedPermission>? Permissions { get; init; }

    public IReadOnlyList<ProvisionedRole>? Roles { get; init; }

    public IReadOnlyList<ProvisionedFeature>? Features { get; init; }

    public IReadOnlyList<ProvisionedPlan>? Plans { get; init; }

    public IReadOnlyList<ProvisionedApiResource>? ApiResources { get; init; }

    public IReadOnlyList<ProvisionedClient>? Clients { get; init; }
}

public sealed record ProvisionedPermission
{
    [Required]
    [StringLength(128)]
    public required string Key { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }
}

public sealed record ProvisionedRole
{
    [Required]
    [StringLength(64)]
    public required string Key { get; init; }

    [Required]
    [StringLength(100)]
    public required string Name { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }

    /// <summary>It cannot be changed once the role exists.</summary>
    public RoleScope Scope { get; init; } = RoleScope.Global;

    /// <summary>Keys of the role's permissions; when omitted, an existing role keeps its permissions.</summary>
    public IReadOnlyList<string>? Permissions { get; init; }
}

public sealed record ProvisionedFeature
{
    [Required]
    [StringLength(64)]
    public required string Key { get; init; }

    [Required]
    [StringLength(100)]
    public required string Name { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }

    /// <summary>It cannot be changed once the feature exists.</summary>
    public required FeatureType Type { get; init; }
}

public sealed record ProvisionedPlan
{
    [Required]
    [StringLength(64)]
    public required string Key { get; init; }

    [Required]
    [StringLength(100)]
    public required string Name { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }

    public bool IsArchived { get; init; }

    /// <summary>A base plan, or an add-on; it cannot be changed once the plan exists.</summary>
    public PlanKind Kind { get; init; }

    /// <summary>
    /// Feature values by key: <c>true</c> or <c>false</c>, and a maximum or <c>null</c> (unlimited) for limits. Features
    /// left out are off, or zero. When the property is omitted, an existing plan keeps its values.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Features { get; init; }
}

public sealed record ProvisionedApiResource
{
    [Required]
    [StringLength(100)]
    public required string Scope { get; init; }

    /// <summary>The scope when omitted. It cannot be changed once the API resource exists.</summary>
    [StringLength(200)]
    public string? Audience { get; init; }

    [StringLength(100)]
    public string? DisplayName { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }
}

public sealed record ProvisionedClient
{
    [Required]
    [StringLength(100)]
    public required string ClientId { get; init; }

    [Required]
    [StringLength(100)]
    public required string DisplayName { get; init; }

    /// <summary>It cannot be changed once the client exists.</summary>
    public required ClientType Type { get; init; }

    public bool FirstParty { get; init; } = true;

    [MaxLength(20)]
    public IReadOnlyList<string> RedirectUris { get; init; } = [];

    [MaxLength(20)]
    public IReadOnlyList<string> PostLogoutRedirectUris { get; init; } = [];

    [MaxLength(50)]
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>Whether every sign-in happens in an organization.</summary>
    public bool RequireOrganization { get; init; }

    /// <summary>Whether the client must push its authorization requests (RFC 9126).</summary>
    public bool RequirePushedAuthorization { get; init; }

    /// <summary>Whether a web or service client may exchange users' access tokens for tokens to other APIs (RFC 8693).</summary>
    public bool AllowTokenExchange { get; init; }

    /// <summary>Where a web client receives logout tokens when the user's session ends.</summary>
    [StringLength(2000)]
    public string? BackChannelLogoutUri { get; init; }

    /// <summary>Keys of a service client's global roles; when omitted, an existing client keeps its roles.</summary>
    [MaxLength(50)]
    public IReadOnlyList<string>? Roles { get; init; }

    /// <summary>
    /// The secret of a web or service client, at least 16 characters. When omitted, a new client gets a random secret
    /// (regenerate it through the API to learn one) and an existing client keeps its secret. Never exported. In a file
    /// applied at startup, <c>${Some:Setting}</c> reads the secret from configuration, such as the environment variable
    /// <c>Some__Setting</c>.
    /// </summary>
    [StringLength(256)]
    public string? ClientSecret { get; init; }

    /// <summary>
    /// The public keys of a web or service client that authenticates with keys instead of a secret
    /// (<c>private_key_jwt</c>): a JWK Set, as in the Management API. When omitted, an existing client keeps its keys.
    /// </summary>
    public JsonObject? JsonWebKeySet { get; init; }
}

/// <summary>What applying a provisioning document changed.</summary>
public sealed record ProvisioningResult(int Created, int Updated, int Unchanged);
