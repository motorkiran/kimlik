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

    public static readonly Error OrganizationNotSupported = Error.Validation(
        "client.organization_not_supported", "Service clients act on their own behalf, outside any organization.");

    public static readonly Error NotConfidential = Error.Validation("client.not_confidential", "Only web and service clients have a secret.");

    public static readonly Error WeakSecret = Error.Validation("client.weak_secret", "A client secret is at least 16 characters.");

    public static readonly Error KeysNotSupported = Error.Validation(
        "client.keys_not_supported", "Only web and service clients authenticate with keys; SPAs and native apps are public.");

    public static readonly Error InvalidKeys = Error.Validation(
        "client.invalid_keys",
        $"Keys are a JWK Set of 1 to {ClientKeys.MaximumCount} public signing keys: RSA of 2048 bits or more, or EC on P-256, P-384 or P-521, "
        + "without private parameters.");

    public static readonly Error SecretOrKeys = Error.Validation("client.secret_or_keys", "A client authenticates with a secret or with keys, not both.");

    public static readonly Error KeysRequired = Error.Validation(
        "client.keys_required", "Keys cannot be removed; generate a new secret to authenticate with a secret again.");

    public static readonly Error PushedAuthorizationNotSupported = Error.Validation(
        "client.pushed_authorization_not_supported", "Service clients do not sign users in, so they make no authorization requests.");

    public static readonly Error TokenExchangeNotSupported = Error.Validation(
        "client.token_exchange_not_supported", "Only web and service clients, which keep a secret or keys, may exchange tokens.");

    public static readonly Error BackChannelLogoutNotSupported = Error.Validation(
        "client.back_channel_logout_not_supported", "Only web clients, which have a server to receive them, get logout tokens.");

    public static readonly Error InvalidBackChannelLogoutUri = Error.Validation(
        "client.invalid_back_channel_logout_uri", "The back-channel logout URI is absolute, without a fragment, and uses HTTPS, or HTTP for localhost.");
}
