using Kimlik.Contracts;

namespace Kimlik.Server.Oidc;

/// <summary>
/// Kimlik's own API as an audience. Its tokens carry the user's system permissions, so they go to it only for the
/// <c>kimlik</c> scope: another scope whose API resource names the same audience, as one registered before it was
/// refused could, does not lead there.
/// </summary>
internal static class KimlikAudience
{
    public static IReadOnlyList<string> Filter(IEnumerable<string> resources, IEnumerable<string> scopes) =>
        scopes.Contains(KimlikScopes.Api, StringComparer.Ordinal)
            ? [.. resources]
            : [.. resources.Where(resource => !string.Equals(resource, KimlikScopes.Api, StringComparison.OrdinalIgnoreCase))];
}
