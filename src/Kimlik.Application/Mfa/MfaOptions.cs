namespace Kimlik.Application.Mfa;

/// <summary>Multi-factor authentication policies, from the <c>Kimlik:Mfa</c> configuration section.</summary>
public sealed class MfaOptions
{
    public const string SectionName = "Kimlik:Mfa";

    /// <summary>Users whose global roles carry system permissions enroll a second factor at sign-in.</summary>
    public bool RequireForAdministrators { get; set; } = true;

    /// <summary>Every user enrolls a second factor at sign-in.</summary>
    public bool RequireForEveryone { get; set; }

    /// <summary>How long a browser can skip the second factor once the user chose to trust it; zero turns this off.</summary>
    public TimeSpan RememberBrowserFor { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Whether administrators may skip the second factor on trusted browsers too.</summary>
    public bool RememberBrowserForAdministrators { get; set; }
}
