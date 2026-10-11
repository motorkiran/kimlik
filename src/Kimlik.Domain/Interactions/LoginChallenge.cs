using System.Security.Cryptography;
using System.Text;
using Kimlik.Domain.Common;

namespace Kimlik.Domain.Interactions;

public enum LoginChallengeStatus
{
    /// <summary>Waiting for the login app to accept or reject it.</summary>
    Pending,

    Accepted,

    Rejected,

    /// <summary>The browser came back and the sign-in went on; it cannot be used again.</summary>
    Completed,
}

/// <summary>How strong a sign-in the authorization request asked for, with <c>acr_values</c>.</summary>
public enum RequestedStrength
{
    Any,

    /// <summary>A second factor, or a passkey.</summary>
    MultiFactor,

    /// <summary>A passkey.</summary>
    PhishingResistant,
}

/// <summary>
/// An authorization request that needs a sign-in, handed to the installation's login app. The challenge itself is a
/// random value that only its hash is kept of, and the browser that was sent to the login app holds a secret, kept
/// hashed here too, without which the challenge cannot be completed.
/// </summary>
public sealed class LoginChallenge
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>How many times a login app can check credentials for one challenge.</summary>
    public const int MaxAttempts = 10;

    /// <summary>The methods a login app can say a person used, as <c>amr</c> values (RFC 8176).</summary>
    public static readonly IReadOnlySet<string> Methods = new HashSet<string>(StringComparer.Ordinal) { "pwd", "otp", "fed", "email", "sms", "pop" };

    // Used by EF Core.
    private LoginChallenge()
    {
    }

    public Guid Id { get; private init; }

    public string ChallengeHash { get; private init; } = string.Empty;

    public string BrowserHash { get; private init; } = string.Empty;

    public string ClientId { get; private init; } = string.Empty;

    /// <summary>The authorization request to come back to, as a local URL.</summary>
    public string ReturnUrl { get; private init; } = string.Empty;

    public List<string> Scopes { get; private init; } = [];

    public string? LoginHint { get; private init; }

    public string? Prompt { get; private init; }

    public RequestedStrength RequestedStrength { get; private init; }

    public LoginChallengeStatus Status { get; private set; }

    public Guid? UserId { get; private set; }

    /// <summary>The methods the login app said the person used.</summary>
    public List<string> AcceptedMethods { get; private set; } = [];

    /// <summary>The client ID of the login app that accepted or rejected the challenge.</summary>
    public string? DecidedBy { get; private set; }

    public string? RejectionReason { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ExpiresAt { get; private init; }

    public static LoginChallenge Create(
        string challenge,
        string browserSecret,
        string clientId,
        string returnUrl,
        IEnumerable<string> scopes,
        string? loginHint,
        string? prompt,
        RequestedStrength strength,
        DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(now),
            ChallengeHash = Hash(challenge),
            BrowserHash = Hash(browserSecret),
            ClientId = clientId,
            ReturnUrl = returnUrl,
            Scopes = [.. scopes],
            LoginHint = loginHint,
            Prompt = prompt,
            RequestedStrength = strength,
            Status = LoginChallengeStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public bool IsPending(DateTimeOffset now) => Status == LoginChallengeStatus.Pending && now < ExpiresAt;

    public bool IsHeldBy(string? browserSecret) =>
        browserSecret is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(browserSecret)), Encoding.ASCII.GetBytes(BrowserHash));

    /// <summary>
    /// Accepts the challenge for the user, signed in with <paramref name="methods"/>. They must meet the strength the
    /// request asked for, and a second factor when <paramref name="secondFactorRequired"/>: two methods, or a passkey.
    /// </summary>
    public Result Accept(Guid userId, IReadOnlyCollection<string> methods, bool secondFactorRequired, string decidedBy, DateTimeOffset now)
    {
        if (!IsPending(now))
        {
            return InteractionErrors.ChallengeNotFound;
        }

        var used = methods.Distinct(StringComparer.Ordinal).ToList();
        // A sign-in starts with a first factor: a password, a code by email or text message, another provider or a passkey.
        if (used.Count == 0 || !used.All(Methods.Contains) || !used.Any(method => method is "pwd" or "email" or "sms" or "fed" or "pop"))
        {
            return InteractionErrors.InvalidMethods;
        }

        var passkey = used.Contains("pop", StringComparer.Ordinal);
        var multiFactor = passkey || used.Count >= 2;
        if ((RequestedStrength == RequestedStrength.PhishingResistant && !passkey)
            || ((secondFactorRequired || RequestedStrength == RequestedStrength.MultiFactor) && !multiFactor))
        {
            return InteractionErrors.FactorsMissing;
        }

        Status = LoginChallengeStatus.Accepted;
        UserId = userId;
        AcceptedMethods = used;
        DecidedBy = decidedBy;
        return Result.Success();
    }

    public Result Reject(string? reason, string decidedBy, DateTimeOffset now)
    {
        if (!IsPending(now))
        {
            return InteractionErrors.ChallengeNotFound;
        }

        Status = LoginChallengeStatus.Rejected;
        RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 256)];
        DecidedBy = decidedBy;
        return Result.Success();
    }

    /// <summary>Ends the challenge once the browser came back, so that it cannot be used again.</summary>
    public void Complete() => Status = LoginChallengeStatus.Completed;
}
