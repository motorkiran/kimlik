using Kimlik.Domain.Common;

namespace Kimlik.Application.Clients;

public static class ClientErrors
{
    public static readonly Error NotFound = Error.NotFound("client.not_found", "The client does not exist.");

    public static readonly Error AlreadyExists = Error.Conflict("client.already_exists", "A client with this client ID already exists.");

    public static readonly Error InvalidClientId = Error.Validation(
        "client.invalid_client_id", "Client IDs are letters, digits, '.', '_' and '-', starting with a letter or digit.");

    public static readonly Error InvalidDisplayName = Error.Validation("client.invalid_display_name", "A display name is required and is at most 100 characters.");

    public static readonly Error RedirectUriRequired = Error.Validation(
        "client.redirect_uri_required", "Apps that sign users in need at least one redirect URI.");

    public static readonly Error InvalidRedirectUri = Error.Validation(
        "client.invalid_redirect_uri",
        "Redirect URIs are absolute, without a fragment, and use HTTPS. HTTP is allowed for localhost, and native apps may use "
        + "a private-use scheme in reverse domain notation, such as com.example.app:/callback.");

    public static readonly Error RedirectUrisNotSupported = Error.Validation(
        "client.redirect_uris_not_supported", "Service clients do not sign users in, so they have no redirect URIs.");

    public static readonly Error UnknownScope = Error.Validation(
        "client.unknown_scope", "One or more scopes are neither OpenID Connect scopes nor the scope of an API resource.");

    public static readonly Error UserScopeNotSupported = Error.Validation(
        "client.user_scope_not_supported", "Service clients act on their own behalf and cannot request user scopes such as openid.");

    public static readonly Error RolesNotSupported = Error.Validation(
        "client.roles_not_supported", "Only service clients hold roles; other clients act for signed-in users.");

    public static readonly Error NotConfidential = Error.Validation("client.not_confidential", "Only web and service clients have a secret.");

    public static readonly Error WeakSecret = Error.Validation("client.weak_secret", "A client secret is at least 16 characters.");
}
