using Kimlik.Application.Abstractions;
using Kimlik.Server.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Identity;

/// <summary>Builds links to the hosted pages from the configured public URL, never from request headers.</summary>
internal sealed class AccountLinks(IOptions<ServerOptions> server) : IAccountLinks
{
    public Uri SignIn() => Build("signin");

    public Uri EmailVerification(Guid userId, string token, string? returnUrl) => Build(
        "verify-email",
        ("userId", userId.ToString()),
        ("token", token),
        ("returnUrl", IsLocalUrl(returnUrl) ? returnUrl : null));

    public Uri PasswordReset(Guid userId, string token) => Build("reset-password", ("userId", userId.ToString()), ("token", token));

    public Uri Invitation(string token) => Build("invitations/accept", ("token", token));

    /// <summary>
    /// A path on this site: rooted, and not a protocol-relative or backslash URL that browsers treat as another host,
    /// nor one with control characters, which browsers drop (<c>/\t/example.com</c> becomes <c>//example.com</c>).
    /// </summary>
    public static bool IsLocalUrl(string? url) =>
        url is { Length: > 0 } && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\')) && !url.Any(char.IsControl);

    private Uri Build(string path, params (string Name, string? Value)[] parameters)
    {
        var publicUrl = server.Value.PublicUrl!.AbsoluteUri;
        var baseUrl = new Uri(publicUrl.EndsWith('/') ? publicUrl : publicUrl + "/");

        return new Uri(QueryHelpers.AddQueryString(
            new Uri(baseUrl, path).AbsoluteUri,
            parameters.Where(parameter => parameter.Value is not null).Select(parameter => new KeyValuePair<string, string?>(parameter.Name, parameter.Value))));
    }
}
