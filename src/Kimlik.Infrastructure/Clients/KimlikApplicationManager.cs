using Kimlik.Application.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;

namespace Kimlik.Infrastructure.Clients;

/// <summary>
/// OpenIddict's application manager, which also accepts a client's previous secret until it expires, so that a client
/// switches to a new secret without downtime.
/// </summary>
internal sealed class KimlikApplicationManager(
    IOpenIddictApplicationCache<OpenIddictEntityFrameworkCoreApplication<Guid>> cache,
    ILogger<OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication<Guid>>> logger,
    IOptionsMonitor<OpenIddictCoreOptions> options,
    IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication<Guid>> store,
    TimeProvider timeProvider)
    : OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication<Guid>>(cache, logger, options, store)
{
    public override async ValueTask<bool> ValidateClientSecretAsync(
        OpenIddictEntityFrameworkCoreApplication<Guid> application, string secret, CancellationToken cancellationToken = default)
    {
        if (await base.ValidateClientSecretAsync(application, secret, cancellationToken))
        {
            return true;
        }

        return ClientPresets.PreviousSecretOf(await GetPropertiesAsync(application, cancellationToken)) is { } previous
            && timeProvider.GetUtcNow() < previous.ExpiresAt
            && await ValidateClientSecretAsync(secret, previous.Hash, cancellationToken);
    }
}

public static class KimlikApplicationManagerExtensions
{
    /// <summary>Uses <see cref="KimlikApplicationManager"/>, so previous client secrets keep working for a while after a rotation.</summary>
    public static OpenIddictCoreBuilder UseKimlikApplicationManager(this OpenIddictCoreBuilder builder) =>
        builder.ReplaceApplicationManager<OpenIddictEntityFrameworkCoreApplication<Guid>, KimlikApplicationManager>();
}
