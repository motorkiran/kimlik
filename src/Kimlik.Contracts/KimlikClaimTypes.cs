namespace Kimlik.Contracts;

/// <summary>Kimlik-specific claims in access tokens, next to the standard OpenID Connect ones.</summary>
public static class KimlikClaimTypes
{
    /// <summary>Keys of the global roles of the subject (a user or a service client).</summary>
    public const string Roles = "roles";

    /// <summary>
    /// The effective permissions of the subject: the union of the permissions of its global roles and, in an
    /// organization context, of its roles in that organization.
    /// </summary>
    public const string Permissions = "permissions";

    /// <summary>The ID of the organization the token acts in; absent without an organization context.</summary>
    public const string OrganizationId = "org_id";

    /// <summary>Keys of the user's roles in the organization the token acts in.</summary>
    public const string OrganizationRoles = "org_roles";

    /// <summary>
    /// The key of the plan in effect: the organization's in an organization context, otherwise the user's. Feature
    /// values come from the plan definitions, which keeps tokens small.
    /// </summary>
    public const string Plan = "plan";

    /// <summary>
    /// The ID of the API key the caller authenticated with, in the principals that Kimlik.AspNetCore builds for API
    /// keys. A user's key has the user as its subject; an organization's key acts on its own behalf and is its own
    /// subject.
    /// </summary>
    public const string ApiKeyId = "api_key_id";
}

/// <summary>Kimlik-specific parameters of authorization and token requests.</summary>
public static class KimlikParameters
{
    /// <summary>
    /// The organization to act in, by ID or slug. In an authorization request it sets the organization context; in
    /// a refresh token request it switches to another organization without a redirect.
    /// </summary>
    public const string Organization = "organization";
}

/// <summary>Scopes defined by Kimlik itself.</summary>
public static class KimlikScopes
{
    /// <summary>
    /// Access to Kimlik's own APIs. It is also the audience of those tokens, and the only audience whose tokens
    /// carry system permissions (<c>kimlik.*</c>).
    /// </summary>
    public const string Api = "kimlik";
}
