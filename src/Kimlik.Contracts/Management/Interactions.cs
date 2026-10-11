using System.ComponentModel.DataAnnotations;

namespace Kimlik.Contracts.Management;

/// <summary>
/// What a login challenge asks for: a sign-in for <c>ClientId</c>, with <c>Scopes</c>, and the <c>LoginHint</c>,
/// <c>Prompt</c> and <c>AcrValues</c> the app sent. <c>SecondFactorRequired</c> says that the sign-in must take a second
/// factor or a passkey, as the app asked; <c>PasskeyRequired</c> that it must take a passkey. <c>HostedSignInUrl</c> signs the
/// person in on Kimlik's own pages instead, for passkeys and for administrators, whom a login app cannot sign in.
/// </summary>
public sealed record LoginChallengeResponse(
    string ClientId,
    string? ClientName,
    IReadOnlyList<string> Scopes,
    string? LoginHint,
    string? Prompt,
    bool SecondFactorRequired,
    bool PasskeyRequired,
    DateTimeOffset ExpiresAt,
    string HostedSignInUrl);

public sealed record AcceptLoginChallengeRequest
{
    [Required]
    public required Guid UserId { get; init; }

    /// <summary>
    /// How the person signed in, as <c>amr</c> values: one or more of <c>pwd</c>, <c>otp</c>, <c>fed</c>, <c>email</c>,
    /// <c>sms</c> and <c>pop</c>. Two of them, or <c>pop</c>, make a multi-factor sign-in.
    /// </summary>
    [Required]
    public required IReadOnlyList<string> Methods { get; init; }
}

public sealed record RejectLoginChallengeRequest
{
    /// <summary>Why, for the app, as the <c>error_description</c> of its <c>access_denied</c> error.</summary>
    [StringLength(256)]
    public string? Reason { get; init; }
}

/// <summary>Where the login app sends the browser next, to finish the sign-in on Kimlik.</summary>
public sealed record LoginChallengeRedirect(string RedirectTo);

public sealed record VerifyPasswordRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public required string Email { get; init; }

    [Required]
    [StringLength(1024)]
    public required string Password { get; init; }
}

/// <summary>
/// The person whose password was right. <c>SecondFactorRequired</c> says that they must verify a second factor before the
/// challenge can be accepted, because they have one or Kimlik's policies require one; <c>HasAuthenticator</c> that they
/// can verify it with an authenticator code.
/// </summary>
public sealed record VerifiedUserResponse(Guid UserId, bool SecondFactorRequired, bool HasAuthenticator);

public sealed record VerifySecondFactorRequest
{
    [Required]
    public required Guid UserId { get; init; }

    /// <summary>A code from the authenticator app, or a recovery code.</summary>
    [Required]
    [StringLength(64)]
    public required string Code { get; init; }
}
