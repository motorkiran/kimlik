using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using OpenIddict.Abstractions;

namespace Kimlik.Application.ApiResources;

internal static class ApiResourceMapping
{
    /// <summary>Describes a scope as an API resource; Kimlik gives each API resource a single audience.</summary>
    public static async Task<ApiResourceResponse> ToResponseAsync(this IOpenIddictScopeManager scopes, object scope, CancellationToken cancellationToken)
    {
        var name = (await scopes.GetNameAsync(scope, cancellationToken))!;
        var audiences = await scopes.GetResourcesAsync(scope, cancellationToken);

        return new ApiResourceResponse(
            Guid.Parse((await scopes.GetIdAsync(scope, cancellationToken))!),
            name,
            audiences.FirstOrDefault() ?? name,
            await scopes.GetDisplayNameAsync(scope, cancellationToken),
            await scopes.GetDescriptionAsync(scope, cancellationToken),
            IsSystem: name == KimlikScopes.Api);
    }
}
