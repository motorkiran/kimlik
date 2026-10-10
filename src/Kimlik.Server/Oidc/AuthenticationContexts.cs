namespace Kimlik.Server.Oidc;

/// <summary>
/// The authentication context classes that apps can ask for with <c>acr_values</c>, and that tokens report as
/// <c>acr</c>: the policies of the OpenID Provider Authentication Policy Extension.
/// </summary>
internal static class AuthenticationContexts
{
    /// <summary>A second factor, or a passkey.</summary>
    public const string MultiFactor = "http://schemas.openid.net/pape/policies/2007/06/multi-factor";

    /// <summary>A passkey, as the sign-in or as the second step.</summary>
    public const string PhishingResistant = "http://schemas.openid.net/pape/policies/2007/06/phishing-resistant";

    public static readonly string[] Supported = [PhishingResistant, MultiFactor];

    /// <summary>The strongest class that a sign-in with these authentication methods (<c>amr</c>) meets, if any.</summary>
    public static string? Of(IReadOnlyList<string> methods) =>
        methods.Contains("pop", StringComparer.Ordinal) ? PhishingResistant
        : methods.Contains("mfa", StringComparer.Ordinal) ? MultiFactor
        : null;
}
