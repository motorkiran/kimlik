namespace Kimlik.Server.Hosting;

/// <summary>Host settings from the <c>Kimlik:Server</c> configuration section.</summary>
public sealed class ServerOptions
{
    public const string SectionName = "Kimlik:Server";

    /// <summary>
    /// The public base URL of this installation, such as <c>https://id.example.com/</c>. It is the token
    /// issuer and the base of every link Kimlik sends, so it is never inferred from request headers.
    /// </summary>
    public Uri? PublicUrl { get; set; }

    /// <summary>
    /// Rejects plain HTTP. Only disable it for local development; behind a TLS-terminating proxy, enable
    /// forwarded headers (<c>ASPNETCORE_FORWARDEDHEADERS_ENABLED=true</c>) instead.
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    internal bool IsValid() =>
        PublicUrl is { IsAbsoluteUri: true }
        && (PublicUrl.Scheme == Uri.UriSchemeHttps || (!RequireHttps && PublicUrl.Scheme == Uri.UriSchemeHttp));
}
