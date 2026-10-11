using Kimlik.Contracts;

namespace Kimlik.Server.Oidc;

/// <summary>
/// Kimlik's own API as an audience. Its tokens reach Kimlik's API, and those for <c>kimlik</c> carry the user's system
/// permissions, so each of its audiences goes only to the scope of the same name: another scope whose API resource
/// names one, as one registered before it was refused could, does not lead there.
/// </summary>
internal static class KimlikAudience
{
    public static IReadOnlyList<string> Filter(IEnumerable<string> resources, IEnumerable<string> scopes) =>
        [.. resources.Where(resource => !KimlikScopes.IsReserved(resource) || scopes.Contains(resource, StringComparer.OrdinalIgnoreCase))];
}
