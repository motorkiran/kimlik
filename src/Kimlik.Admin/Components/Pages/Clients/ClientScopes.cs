using Kimlik.Admin.Security;
using Kimlik.Application.ApiResources;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;

namespace Kimlik.Admin.Components.Pages.Clients;

/// <summary>The scopes a client can be allowed: OpenID Connect's own and those of the registered API resources.</summary>
internal static class ClientScopes
{
    public static readonly string[] Standard = ["openid", "profile", "email", "phone", "offline_access"];

    public static async Task<IReadOnlyList<string>> AllAsync(AdminOperations operations, CancellationToken cancellationToken)
    {
        var resources = await operations.RunAsync<ListApiResourcesHandler, Result<Page<ApiResourceResponse>>>(
            SystemPermissions.ClientsRead, handler => handler.HandleAsync(new ListApiResourcesQuery(null, null, 200), cancellationToken));

        return [.. Standard, .. resources.IsSuccess ? resources.Value.Items.Select(resource => resource.Scope) : []];
    }

    /// <summary>One URI per line, as typed in a text area.</summary>
    public static IReadOnlyList<string> Lines(string? text) =>
        [.. (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
