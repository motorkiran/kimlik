using Kimlik.Domain.Common;

namespace Kimlik.Domain.ApiKeys;

public static class ApiKeyErrors
{
    public static readonly Error NotFound = Error.NotFound("api_key.not_found", "The API key does not exist.");

    public static readonly Error InvalidName = Error.Validation("api_key.invalid_name", "A name is required and is at most 100 characters.");

    public static readonly Error ExpiryInThePast = Error.Validation("api_key.expiry_in_the_past", "The expiry date must be in the future.");

    public static readonly Error SystemPermission = Error.Validation(
        "api_key.system_permission", "API keys call the application's API; they cannot hold Kimlik's own 'kimlik.' permissions.");

    public static readonly Error LimitReached = Error.Conflict(
        "api_key.limit_reached", "The owner already has 100 API keys that are not revoked; revoke one first.");

    /// <summary>Nobody gives a key more than they may do themselves.</summary>
    public static readonly Error PermissionNotHeld = Error.Forbidden(
        "api_key.permission_not_held", "A key can only have permissions that its creator holds.");
}
