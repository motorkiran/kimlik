using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kimlik.Application.Abstractions;
using Kimlik.Contracts;
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
    IReadOnlyList<string> Scopes,
    bool RequireOrganization);

/// <summary>
/// Turns a client type into OpenIddict settings, and back. The type is not stored: it is read from the settings
/// it produced, so the two cannot drift apart.
/// </summary>
public static partial class ClientPresets
{
    /// <summary>The application property, in OpenIddict's custom properties, that makes sign-ins require an organization.</summary>
    public const string RequireOrganizationProperty = "kimlik_require_organization";

    private const int UriMaxLength = 2000;

    /// <summary>Secrets that people choose, as in a provisioning file, must be at least this long.</summary>
    private const int SecretMinimumLength = 16;

    /// <summary>The scopes of apps that sign users in. <c>openid</c> needs no permission in OpenIddict.</summary>
    private static readonly HashSet<string> UserScopes = new(StringComparer.Ordinal) { Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess };

    internal static bool IsConfidential(ClientType type) => type is ClientType.Web or ClientType.Service;

    internal static bool SignsInUsers(ClientType type) => type is not ClientType.Service;

    /// <summary>
    /// Whether the client gets tokens for Kimlik's own API on behalf of the users who sign in to it. Those tokens
    /// carry everything the user may do in Kimlik, an administrator's included, so only callers with full access
    /// may set up such a client or change where its tokens go.
    /// </summary>
    internal static bool ActsForUsersInKimlik(ClientType type, IEnumerable<string> scopes) =>
        SignsInUsers(type) && scopes.Contains(KimlikScopes.Api, StringComparer.Ordinal);

    internal static bool IsValidClientId(string clientId) => ClientIdPattern().IsMatch(clientId);

    /// <summary>A 256-bit secret, which OpenIddict stores hashed.</summary>
    internal static string GenerateSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    internal static Result CheckSecret(ClientType type, string secret)
    {
        if (!IsConfidential(type))
        {
            return ClientErrors.NotConfidential;
        }

        return secret.Length >= SecretMinimumLength ? Result.Success() : ClientErrors.WeakSecret;
    }

    internal static async Task<Result> ValidateAsync(IKimlikDbContext context, ClientType type, ClientSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.DisplayName) || settings.DisplayName.Trim().Length > 100)
        {
            return ClientErrors.InvalidDisplayName;
        }

        if (!SignsInUsers(type) && (settings.RedirectUris.Count > 0 || settings.PostLogoutRedirectUris.Count > 0))
        {
            return ClientErrors.RedirectUrisNotSupported;
        }

        // A native client without one, such as a command-line tool, signs people in through the device flow only.
        if (SignsInUsers(type) && type != ClientType.Native && settings.RedirectUris.Count == 0)
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

        if (!SignsInUsers(type) && settings.RequireOrganization)
        {
            return ClientErrors.OrganizationNotSupported;
        }

        var apiScopes = settings.Scopes.Where(scope => !UserScopes.Contains(scope)).Distinct(StringComparer.Ordinal).ToList();
        var registered = await context.Scopes.CountAsync(scope => apiScopes.Contains(scope.Name!), cancellationToken);

        return registered == apiScopes.Count ? Result.Success() : ClientErrors.UnknownScope;
    }

    /// <summary>Applies the type and settings to <paramref name="descriptor"/>, replacing its grants, URIs and scopes.</summary>
    internal static void Apply(OpenIddictApplicationDescriptor descriptor, ClientType type, ClientSettings settings)
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

            // Apps on devices without a browser, or command-line tools, sign people in from another device (RFC 8628).
            if (type == ClientType.Native)
            {
                descriptor.Permissions.UnionWith([OidcPermissions.Endpoints.DeviceAuthorization, OidcPermissions.GrantTypes.DeviceCode]);
            }
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

        descriptor.Properties.Remove(RequireOrganizationProperty);
        if (settings.RequireOrganization)
        {
            descriptor.Properties[RequireOrganizationProperty] = JsonSerializer.SerializeToElement(true);
        }
    }

    /// <summary>Whether sign-ins to the client must happen in an organization.</summary>
    public static bool RequiresOrganization(IReadOnlyDictionary<string, JsonElement> properties) =>
        properties.TryGetValue(RequireOrganizationProperty, out var value) && value.ValueKind == JsonValueKind.True;

    internal static ClientType TypeOf(string? clientType, string? applicationType, IEnumerable<string> permissions) => clientType switch
    {
        ClientTypes.Public => applicationType == ApplicationTypes.Native ? ClientType.Native : ClientType.Spa,
        _ => permissions.Contains(OidcPermissions.GrantTypes.ClientCredentials, StringComparer.Ordinal) ? ClientType.Service : ClientType.Web,
    };

    /// <summary>The scopes a client may request; apps that sign users in may always request <c>openid</c>.</summary>
    internal static IReadOnlyList<string> ScopesOf(ClientType type, IEnumerable<string> permissions) =>
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
