using Kimlik.Domain.Common;

namespace Kimlik.Application.Mfa;

public static class MfaErrors
{
    public static readonly Error AlreadyEnabled = Error.Conflict(
        "mfa.already_enabled", "Two-factor authentication is already on; turn it off before setting up a new authenticator.");

    public static readonly Error NotEnabled = Error.Validation("mfa.not_enabled", "Two-factor authentication is off.");

    public static readonly Error SetupNotStarted = Error.Validation("mfa.setup_not_started", "Set up an authenticator before confirming it.");

    public static readonly Error InvalidCode = Error.Validation("mfa.invalid_code", "The code is wrong or was already used.");

    public static readonly Error Required = Error.Forbidden("mfa.required", "Two-factor authentication is required for this account and cannot be turned off.");
}
