namespace Kimlik.AspNetCore;

/// <summary>Which Kimlik installation issues the access tokens this API accepts, and for which audience.</summary>
public sealed class KimlikOptions
{
    /// <summary>The base address of the Kimlik installation, which is also its token issuer.</summary>
    public Uri? Authority { get; set; }

    /// <summary>The audience of this API, as registered in Kimlik; tokens for other APIs are rejected.</summary>
    public string? Audience { get; set; }

    /// <summary>Whether Kimlik's metadata must be fetched over HTTPS; turn it off only for local development.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Whether, and how, this API accepts Kimlik API keys next to access tokens.</summary>
    public KimlikApiKeyOptions ApiKeys { get; } = new();

    internal bool IsValid() => Authority is { IsAbsoluteUri: true } && !string.IsNullOrWhiteSpace(Audience);
}

/// <summary>
/// API keys, sent as <c>Authorization: Bearer kmk_…</c>. Kimlik verifies them through Kimlik.Client, so register it
/// with <c>AddKimlikClient</c> as a service client holding <c>kimlik.api_keys:verify</c>.
/// </summary>
public sealed class KimlikApiKeyOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// How long Kimlik's answer about a key is reused; zero asks Kimlik every time. A revoked key, or a permission its
    /// owner lost, can keep working for this long.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(30);
}

public static class KimlikDefaults
{
    /// <summary>The authentication scheme <see cref="KimlikServiceCollectionExtensions.AddKimlik"/> registers.</summary>
    public const string AuthenticationScheme = "Kimlik";

    /// <summary>The scheme that requests with a Kimlik API key are forwarded to, when API keys are enabled.</summary>
    public const string ApiKeyAuthenticationScheme = "Kimlik.ApiKey";
}
