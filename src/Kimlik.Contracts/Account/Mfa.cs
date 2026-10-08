using System.ComponentModel.DataAnnotations;

namespace Kimlik.Contracts.Account;

/// <summary>The signed-in user's two-factor authentication.</summary>
public sealed record MfaStatusResponse(bool Enabled, bool Required, int RecoveryCodesLeft);

/// <summary>
/// A new authenticator key. Add it to an authenticator app, by scanning <c>otpAuthUri</c> as a QR code or typing
/// <c>secret</c>, then confirm with a code from the app.
/// </summary>
public sealed record AuthenticatorSetupResponse(string Secret, string OtpAuthUri);

/// <summary>Single-use codes for signing in without the authenticator app. They are shown only this once.</summary>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> Codes);

/// <summary>A code from the authenticator app, proving the request comes from someone holding it.</summary>
public sealed record AuthenticatorCodeRequest
{
    [Required]
    [StringLength(16)]
    public required string Code { get; init; }
}
