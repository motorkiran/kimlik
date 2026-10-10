using Kimlik.Domain.Common;

namespace Kimlik.Domain.Organizations;

public static class SsoErrors
{
    public static readonly Error NotFound = Error.NotFound("sso.not_found", "The SSO connection does not exist.");

    public static readonly Error InvalidName = Error.Validation("sso.invalid_name", "A name is required and is at most 100 characters.");

    public static readonly Error InvalidIssuer = Error.Validation(
        "sso.invalid_issuer", "The issuer is the provider's HTTPS URL, without a query or fragment, such as 'https://login.microsoftonline.com/{tenant}/v2.0'.");

    public static readonly Error InvalidClientId = Error.Validation("sso.invalid_client_id", "A client ID is required and is at most 256 characters.");

    public static readonly Error InvalidClientSecret = Error.Validation("sso.invalid_client_secret", "A client secret is required and is at most 1024 characters.");

    public static readonly Error InvalidEntityId = Error.Validation(
        "sso.invalid_entity_id", "The provider's entity ID is required and is at most 512 characters.");

    public static readonly Error InvalidSignOnUrl = Error.Validation(
        "sso.invalid_sign_on_url", "The sign-on URL is the provider's HTTPS URL that takes authentication requests.");

    public static readonly Error InvalidCertificate = Error.Validation(
        "sso.invalid_certificate", "The certificate is the one the provider signs with, in PEM or base64.");

    public static readonly Error InvalidDomains = Error.Validation(
        "sso.invalid_domains", "Between 1 and 20 email domains are required, such as 'acme.com'.");

    public static readonly Error DomainTaken = Error.Conflict("sso.domain_taken", "Another SSO connection covers one of these domains.");
}
