using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kimlik.Application.Abstractions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using OidcPermissions = OpenIddict.Abstractions.OpenIddictConstants.Permissions;

namespace Kimlik.Application.Clients;

/// <summary>What can be set on a client after it is created.</summary>
internal sealed record ClientSettings(
    string DisplayName,
    bool FirstParty,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes);

/// <summary>
/// Turns a client type into OpenIddict settings, and back. The type is not stored: it is read from the settings
/// it produced, so the two cannot drift apart.
/// </summary>
internal static partial class ClientPresets
{
    private const int UriMaxLength = 2000;

    /// <summary>The scopes of apps that sign users in. <c>openid</c> needs no permission in OpenIddict.</summary>
    private static readonly HashSet<string> UserScopes = new(StringComparer.Ordinal) { Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess };

    public static bool IsConfidential(ClientType type) => type is ClientType.Web or ClientType.Service;

    public static bool SignsInUsers(ClientType type) => type is not ClientType.Service;

    public static bool IsValidClientId(string clientId) => ClientIdPattern().IsMatch(clientId);

    /// <summary>A 256-bit secret, which OpenIddict stores hashed.</summary>
    public static string GenerateSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static async Task<Result> ValidateAsync(IKimlikDbContext context, ClientType type, ClientSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.DisplayName) || settings.DisplayName.Trim().Length > 100)
        {
            return ClientErrors.InvalidDisplayName;
        }

        if (!SignsInUsers(type) && (settings.RedirectUris.Count > 0 || settings.PostLogoutRedirectUris.Count > 0))
        {
            return ClientErrors.RedirectUrisNotSupported;
        }

        if (SignsInUsers(type) && settings.RedirectUris.Count == 0)
        {
            return ClientErrors.RedirectUriRequired;
        }

        if (!settings.RedirectUris.Concat(settings.PostLogoutRedirectUris).All(uri => IsValidRedirectUri(uri, type)))
        {
            return ClientErrors.InvalidRedirectUri;
        }

        if (!SignsInUsers(type) && settings.Scopes.Any(UserScopes.Contains))
        {
            return ClientErrors.UserScopeNotSupported;
        }

        var apiScopes = settings.Scopes.Where(scope => !UserScopes.Contains(scope)).Distinct(StringComparer.Ordinal).ToList();
        var registered = await context.Scopes.CountAsync(scope => apiScopes.Contains(scope.Name!), cancellationToken);

        return registered == apiScopes.Count ? Result.Success() : ClientErrors.UnknownScope;
    }

    /// <summary>Applies the type and settings to <paramref name="descriptor"/>, replacing its grants, URIs and scopes.</summary>
    public static void Apply(OpenIddictApplicationDescriptor descriptor, ClientType type, ClientSettings settings)
    {
        descriptor.ClientType = IsConfidential(type) ? ClientTypes.Confidential : ClientTypes.Public;
        descriptor.ApplicationType = type == ClientType.Native ? ApplicationTypes.Native : ApplicationTypes.Web;
        descriptor.ConsentType = settings.FirstParty ? ConsentTypes.Implicit : ConsentTypes.Explicit;
        descriptor.DisplayName = settings.DisplayName.Trim();
        descriptor.DisplayNames.Clear();

        descriptor.RedirectUris.Clear();
        descriptor.RedirectUris.UnionWith(settings.RedirectUris.Select(uri => new Uri(uri)));
        descriptor.PostLogoutRedirectUris.Clear();
        descriptor.PostLogoutRedirectUris.UnionWith(settings.PostLogoutRedirectUris.Select(uri => new Uri(uri)));

        descriptor.Permissions.Clear();
        descriptor.Requirements.Clear();

        if (SignsInUsers(type))
        {
            descriptor.Permissions.UnionWith(
            [
                OidcPermissions.Endpoints.Authorization,
                OidcPermissions.Endpoints.Token,
                OidcPermissions.Endpoints.EndSession,
                OidcPermissions.Endpoints.Revocation,
                OidcPermissions.GrantTypes.AuthorizationCode,
                OidcPermissions.GrantTypes.RefreshToken,
                OidcPermissions.ResponseTypes.Code,
            ]);
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }
        else
        {
            descriptor.Permissions.UnionWith(
            [
                OidcPermissions.Endpoints.Token,
                OidcPermissions.Endpoints.Introspection,
                OidcPermissions.Endpoints.Revocation,
                OidcPermissions.GrantTypes.ClientCredentials,
            ]);
        }

        descriptor.Permissions.UnionWith(settings.Scopes.Where(scope => scope != Scopes.OpenId).Select(scope => OidcPermissions.Prefixes.Scope + scope));
    }

    public static ClientType TypeOf(string? clientType, string? applicationType, IEnumerable<string> permissions) => clientType switch
    {
        ClientTypes.Public => applicationType == ApplicationTypes.Native ? ClientType.Native : ClientType.Spa,
        _ => permissions.Contains(OidcPermissions.GrantTypes.ClientCredentials, StringComparer.Ordinal) ? ClientType.Service : ClientType.Web,
    };

    /// <summary>The scopes a client may request; apps that sign users in may always request <c>openid</c>.</summary>
    public static IReadOnlyList<string> ScopesOf(ClientType type, IEnumerable<string> permissions) =>
    [
        .. SignsInUsers(type) ? [Scopes.OpenId] : Array.Empty<string>(),
        .. permissions
            .Where(permission => permission.StartsWith(OidcPermissions.Prefixes.Scope, StringComparison.Ordinal))
            .Select(permission => permission[OidcPermissions.Prefixes.Scope.Length..])
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// HTTPS, or HTTP on the loopback interface; native apps may also use a private-use scheme, which RFC 8252
    /// (section 7.1) asks to be in reverse domain name notation.
    /// </summary>
    private static bool IsValidRedirectUri(string value, ClientType type)
    {
        if (value.Length > UriMaxLength || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || value.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            return uri.IsLoopback;
        }

        return type == ClientType.Native && uri.Scheme.Contains('.', StringComparison.Ordinal);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdPattern();
}
