namespace Kimlik.Server.SocialLogin;

/// <summary>
/// Sign-in with accounts at other providers, from the <c>Kimlik:SocialLogin</c> configuration section. A provider is
/// offered once its client ID is set. Register <c>{PublicUrl}/signin/external/callback/{provider}</c> as the redirect
/// URI with the provider, such as <c>https://id.example.com/signin/external/callback/google</c>.
/// </summary>
public sealed class SocialLoginOptions
{
    public const string SectionName = "Kimlik:SocialLogin";

    public SocialProviderOptions Google { get; set; } = new() { TrustEmail = true };

    public MicrosoftProviderOptions Microsoft { get; set; } = new();

    /// <summary>GitHub only hands out addresses that its users verified.</summary>
    public SocialProviderOptions GitHub { get; set; } = new() { TrustEmail = true };

    public AppleProviderOptions Apple { get; set; } = new() { TrustEmail = true };
}

public class SocialProviderOptions
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>
    /// Whether an address the provider verified counts as verified in Kimlik, so new accounts skip email
    /// verification. Only enable it for providers that verify the addresses they hand out; Microsoft, for one, does
    /// not for every account.
    /// </summary>
    public bool TrustEmail { get; set; }

    internal bool IsEnabled => !string.IsNullOrWhiteSpace(ClientId);
}

public sealed class MicrosoftProviderOptions : SocialProviderOptions
{
    /// <summary>
    /// Whose accounts can sign in: <c>common</c> (work, school and personal Microsoft accounts), <c>organizations</c>,
    /// <c>consumers</c>, or one Microsoft Entra tenant by ID.
    /// </summary>
    public string Tenant { get; set; } = "common";
}

/// <summary>Sign in with Apple, where Kimlik signs its own client secret with a key from the Apple developer account.</summary>
public sealed class AppleProviderOptions : SocialProviderOptions
{
    public string? TeamId { get; set; }

    /// <summary>The ID of the key that signs the client secret.</summary>
    public string? KeyId { get; set; }

    /// <summary>The key's private part, the PEM text of the <c>.p8</c> file Apple issued.</summary>
    public string? PrivateKey { get; set; }
}
