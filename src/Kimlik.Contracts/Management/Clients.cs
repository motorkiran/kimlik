using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

/// <summary>The kind of application, which decides how it gets tokens.</summary>
public enum ClientType
{
    /// <summary>A browser app without a backend. Public; signs users in with authorization code and PKCE.</summary>
    Spa,

    /// <summary>A mobile or desktop app. Public; signs users in with authorization code and PKCE.</summary>
    Native,

    /// <summary>A server-side web app. Confidential; signs users in with authorization code and PKCE.</summary>
    Web,

    /// <summary>A backend acting on its own behalf. Confidential; uses client credentials and holds roles.</summary>
    Service,
}

/// <summary>
/// An application that requests tokens. <c>scopes</c> are what it may request; <c>roles</c> are the global
/// roles of a service client.
/// </summary>
public sealed record ClientResponse(
    Guid Id,
    string ClientId,
    string? DisplayName,
    ClientType Type,
    bool FirstParty,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Roles,
    bool RequireOrganization);

public sealed record CreateClientRequest
{
    /// <summary>Letters, digits, <c>.</c>, <c>_</c> and <c>-</c>, such as <c>web</c> or <c>billing-worker</c>. It cannot be changed later.</summary>
    [Required]
    [StringLength(100)]
    public required string ClientId { get; init; }

    [Required]
    [StringLength(100)]
    public required string DisplayName { get; init; }

    /// <summary>It cannot be changed later.</summary>
    public required ClientType Type { get; init; }

    /// <summary>First-party apps are your own and skip the consent screen; third-party apps ask users for consent.</summary>
    public bool FirstParty { get; init; } = true;

    /// <summary>Where users return after signing in; required for apps that sign users in, and matched exactly.</summary>
    [MaxLength(20)]
    public IReadOnlyList<string> RedirectUris { get; init; } = [];

    /// <summary>Where users may return after signing out.</summary>
    [MaxLength(20)]
    public IReadOnlyList<string> PostLogoutRedirectUris { get; init; } = [];

    /// <summary>
    /// What the client may request: <c>openid</c>, <c>profile</c>, <c>email</c> and <c>offline_access</c> for apps
    /// that sign users in, and the scopes of API resources.
    /// </summary>
    [MaxLength(50)]
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>Keys of the global roles of a service client.</summary>
    [MaxLength(50)]
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// Whether every sign-in happens in an organization. When the authorization request names none, the user
    /// chooses one of theirs.
    /// </summary>
    public bool RequireOrganization { get; init; }
}

/// <summary>A new client and, for web and service clients, its secret. The secret is shown only this once.</summary>
public sealed record CreatedClientResponse(ClientResponse Client, string? ClientSecret);

/// <summary>A new client secret, shown only this once. The previous secret stops working at once.</summary>
public sealed record ClientSecretResponse(string ClientSecret);

/// <summary>
/// Changes a client with JSON Merge Patch semantics: an omitted property keeps its value, and <c>null</c> empties
/// a list. Lists are replaced as a whole.
/// </summary>
public sealed record UpdateClientRequest
{
    /// <summary>The new name; a client always has one, so it cannot be cleared.</summary>
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

    public bool? FirstParty { get; init; }

    public bool? RequireOrganization { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? RedirectUris
    {
        get;
        init
        {
            field = value;
            HasRedirectUris = true;
        }
    }

    [MaxLength(20)]
    public IReadOnlyList<string>? PostLogoutRedirectUris
    {
        get;
        init
        {
            field = value;
            HasPostLogoutRedirectUris = true;
        }
    }

    [MaxLength(50)]
    public IReadOnlyList<string>? Scopes
    {
        get;
        init
        {
            field = value;
            HasScopes = true;
        }
    }

    /// <summary>Whether the request sets <see cref="DisplayName"/>.</summary>
    [JsonIgnore]
    public bool HasDisplayName { get; private init; }

    /// <summary>Whether the request sets <see cref="RedirectUris"/>.</summary>
    [JsonIgnore]
    public bool HasRedirectUris { get; private init; }

    /// <summary>Whether the request sets <see cref="PostLogoutRedirectUris"/>.</summary>
    [JsonIgnore]
    public bool HasPostLogoutRedirectUris { get; private init; }

    /// <summary>Whether the request sets <see cref="Scopes"/>.</summary>
    [JsonIgnore]
    public bool HasScopes { get; private init; }
}
