using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Kimlik.Infrastructure.Security.TokenKeys;

public static class TokenKeyServiceCollectionExtensions
{
    /// <summary>
    /// Supplies the OpenID Connect server with the managed token keys. Call it after the server is added:
    /// the active-key ordering must run after OpenIddict's own post-configuration.
    /// </summary>
    public static IServiceCollection AddTokenKeys(this IServiceCollection services, params string[] healthCheckTags)
    {
        services.AddOptions<TokenKeyOptions>()
            .BindConfiguration(TokenKeyOptions.SectionName)
            .Validate(
                options => options.IsValid(),
                $"{TokenKeyOptions.SectionName}: all periods must be positive, the prepublish period must be shorter than "
                + "the rotation interval and the refresh interval shorter than the prepublish period.")
            .ValidateOnStart();

        services.AddSingleton<TokenKeyFactory>();
        services.AddSingleton<TokenKeyRing>();
        services.AddSingleton<ITokenKeyStatus>(provider => provider.GetRequiredService<TokenKeyRing>());
        services.AddSingleton<TokenKeyRefresher>();
        services.AddSingleton<TokenKeyChangeSignal>();
        services.AddSingleton<IOptionsChangeTokenSource<OpenIddictServerOptions>, TokenKeyOptionsChangeTokenSource<OpenIddictServerOptions>>();
        services.AddHostedService<TokenKeyRefreshService>();

        services.AddSingleton<ConfigureTokenKeyCredentials>();
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>>(provider => provider.GetRequiredService<ConfigureTokenKeyCredentials>());
        services.AddSingleton<IPostConfigureOptions<OpenIddictServerOptions>>(provider => provider.GetRequiredService<ConfigureTokenKeyCredentials>());

        services.AddHealthChecks().AddCheck<TokenKeysHealthCheck>("token-keys", HealthStatus.Unhealthy, healthCheckTags);

        return services;
    }
}
