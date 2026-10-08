using System.ComponentModel.DataAnnotations;

namespace Kimlik.Application.Accounts;

/// <summary>Account policies from the <c>Kimlik:Accounts</c> configuration section.</summary>
public sealed class AccountOptions
{
    public const string SectionName = "Kimlik:Accounts";

    public RegistrationMode Registration { get; set; } = RegistrationMode.Open;

    /// <summary>People must confirm their email address before they can sign in.</summary>
    public bool RequireVerifiedEmail { get; set; } = true;

    /// <summary>Length is what makes passwords strong, so there are no composition rules (NIST SP 800-63B).</summary>
    [Range(8, PasswordMaximumLength)]
    public int PasswordMinimumLength { get; set; } = 12;

    /// <summary>Failed sign-ins in a row before the account is locked.</summary>
    [Range(1, 100)]
    public int MaxFailedSignInAttempts { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a "remember me" sign-in session lasts without activity.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// The global roles every new user gets, such as one for customers. They cannot carry system permissions, since anyone
    /// who signs up gets them.
    /// </summary>
    public IReadOnlyList<string> DefaultRoles { get; set; } = [];

    /// <summary>Upper bound that keeps password hashing cost predictable.</summary>
    public const int PasswordMaximumLength = 128;
}

public enum RegistrationMode
{
    /// <summary>Anyone can create an account.</summary>
    Open,

    /// <summary>Accounts are created by administrators or through invitations.</summary>
    InviteOnly,

    Disabled,
}
