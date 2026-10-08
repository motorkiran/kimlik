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

    internal bool IsValid() => Authority is { IsAbsoluteUri: true } && !string.IsNullOrWhiteSpace(Audience);
}

public static class KimlikDefaults
{
    /// <summary>The authentication scheme <see cref="KimlikServiceCollectionExtensions.AddKimlik"/> registers.</summary>
    public const string AuthenticationScheme = "Kimlik";
}
