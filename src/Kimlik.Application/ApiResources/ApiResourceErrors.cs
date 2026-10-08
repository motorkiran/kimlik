using Kimlik.Domain.Common;

namespace Kimlik.Application.ApiResources;

public static class ApiResourceErrors
{
    public static readonly Error NotFound = Error.NotFound("api_resource.not_found", "The API resource does not exist.");

    public static readonly Error AlreadyExists = Error.Conflict("api_resource.already_exists", "An API resource with this scope already exists.");

    public static readonly Error InvalidScope = Error.Validation(
        "api_resource.invalid_scope", "Scopes are lowercase letters, digits, '.', ':', '_' and '-', starting with a letter.");

    public static readonly Error ReservedScope = Error.Validation(
        "api_resource.reserved_scope", "OpenID Connect scopes and 'kimlik' are defined by Kimlik.");

    public static readonly Error InvalidAudience = Error.Validation(
        "api_resource.invalid_audience", "An audience is printable ASCII without spaces, quotes or backslashes.");

    public static readonly Error SystemResourceReadOnly = Error.Forbidden(
        "api_resource.system_read_only", "Kimlik's own API cannot be changed or deleted.");
}
