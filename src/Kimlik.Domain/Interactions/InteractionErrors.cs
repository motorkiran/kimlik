using Kimlik.Domain.Common;

namespace Kimlik.Domain.Interactions;

public static class InteractionErrors
{
    /// <summary>Unknown, expired or already decided: a login app learns no more than that.</summary>
    public static readonly Error ChallengeNotFound = Error.NotFound("interaction.challenge_not_found", "The login challenge does not exist, has expired or was already decided.");

    public static readonly Error InvalidMethods = Error.Validation(
        "interaction.invalid_methods", "Methods are one or more of pwd, otp, fed, email, sms and pop, with a first factor among them: all but otp.");

    public static readonly Error FactorsMissing = Error.Validation(
        "interaction.factors_missing", "The sign-in needs a second factor or a passkey, as the request or Kimlik's policies ask.");

    public static readonly Error UserCannotSignIn = Error.Validation("interaction.user_cannot_sign_in", "The user does not exist, is suspended or has not verified their email address.");

    public static readonly Error InvalidCredentials = Error.Validation("interaction.invalid_credentials", "Invalid email or password.");

    public static readonly Error WrongCode = Error.Validation("interaction.wrong_code", "The code is not right.");

    public static readonly Error LockedOut = Error.Forbidden("interaction.locked_out", "Too many failed attempts; the account is locked for a while.");

    public static readonly Error TooManyAttempts = Error.Forbidden("interaction.too_many_attempts", "The challenge has had as many credential checks as it gets; start again.");
}
