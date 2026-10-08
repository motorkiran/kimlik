using Kimlik.Contracts;

namespace Kimlik.Client;

/// <summary>How to reach Kimlik and authenticate as a service client.</summary>
public sealed class KimlikClientOptions
{
    /// <summary>The base address of the Kimlik installation, which is also its token issuer.</summary>
    public Uri? Authority { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>The scope of the access tokens; the service client must be allowed to request it.</summary>
    public string Scope { get; set; } = KimlikScopes.Api;

    /// <summary>The authority with a trailing slash, so that relative paths resolve beneath it.</summary>
    internal Uri BaseAddress => Authority!.AbsoluteUri.EndsWith('/') ? Authority : new Uri($"{Authority.AbsoluteUri}/");

    internal bool IsValid() =>
        Authority is { IsAbsoluteUri: true } && !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret) && !string.IsNullOrWhiteSpace(Scope);
}
