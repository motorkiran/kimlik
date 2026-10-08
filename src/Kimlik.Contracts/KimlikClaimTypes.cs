namespace Kimlik.Contracts;

/// <summary>Kimlik-specific claims in access tokens, next to the standard OpenID Connect ones.</summary>
public static class KimlikClaimTypes
{
    /// <summary>Keys of the global roles of the subject (a user or a service client).</summary>
    public const string Roles = "roles";

    /// <summary>The effective permissions of the subject: the union of the permissions of its roles.</summary>
    public const string Permissions = "permissions";
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
