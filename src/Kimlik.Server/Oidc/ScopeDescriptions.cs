using Microsoft.Extensions.Localization;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Oidc;

/// <summary>What the scopes an application asks for let it do, in words for the person asked to allow it.</summary>
public sealed class ScopeDescriptions(IOpenIddictScopeManager scopes, IStringLocalizer<SharedResource> localizer)
{
    public async Task<IReadOnlyList<string>> DescribeAsync(IEnumerable<string> requestedScopes, CancellationToken cancellationToken)
    {
        var descriptions = new List<string>();

        foreach (var scope in requestedScopes)
        {
            var description = scope switch
            {
                Scopes.OpenId => localizer["Confirm your identity"].Value,
                Scopes.Profile => localizer["See your name and profile"].Value,
                Scopes.Email => localizer["See your email address"].Value,
                Scopes.OfflineAccess => localizer["Stay signed in to it"].Value,
                _ => await scopes.FindByNameAsync(scope, cancellationToken) is { } apiScope
                    ? await scopes.GetLocalizedDisplayNameAsync(apiScope, cancellationToken) ?? scope
                    : scope,
            };

            descriptions.Add(description);
        }

        return descriptions;
    }
}
