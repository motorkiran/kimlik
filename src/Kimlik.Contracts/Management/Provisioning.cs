using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

/// <summary>
/// The access model of an installation as one document: permissions, roles, API resources and clients.
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
}

/// <summary>What applying a provisioning document changed.</summary>
public sealed record ProvisioningResult(int Created, int Updated, int Unchanged);
