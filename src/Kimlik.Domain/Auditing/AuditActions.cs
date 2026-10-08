namespace Kimlik.Domain.Auditing;

/// <summary>Audit action names, written as <c>resource.past_tense_verb</c>.</summary>
public static class AuditActions
{
    public const string UserCreated = "user.created";
    public const string UserSignedIn = "user.signed_in";
    public const string UserSignInFailed = "user.sign_in_failed";
    public const string UserLockedOut = "user.locked_out";
    public const string UserSignedOut = "user.signed_out";
    public const string UserEmailVerified = "user.email_verified";
    public const string UserPasswordResetRequested = "user.password_reset_requested";
    public const string UserPasswordReset = "user.password_reset";
}
