using Kimlik.Infrastructure.Persistence;
using Kimlik.Infrastructure.Security.TokenKeys;
using Kimlik.Server.Diagnostics;
using Kimlik.Server.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Oidc;

internal static class OidcServiceCollectionExtensions
{
    public static IServiceCollection AddOidcServer(this IServiceCollection services)
    {
        services.AddOptions<ServerOptions>()
            .BindConfiguration(ServerOptions.SectionName)
            .Validate(
                options => options.IsValid(),
                $"{ServerOptions.SectionName}:PublicUrl must be an absolute URL, using HTTPS unless RequireHttps is false.")
            .ValidateOnStart();

        services.AddOptions<TokenOptions>()
            .BindConfiguration(TokenOptions.SectionName)
            .Validate(options => options.IsValid(), $"{TokenOptions.SectionName}: lifetimes must be positive and the absolute refresh token lifetime at least the sliding one.")
            .Validate<IOptions<TokenKeyOptions>>(
                (tokens, keys) => keys.Value.RetentionPeriod >= tokens.RefreshTokenAbsoluteLifetime,
                $"{TokenKeyOptions.SectionName}:RetentionPeriod must cover {TokenOptions.SectionName}:RefreshTokenAbsoluteLifetime, "
                + "or refresh tokens would outlive the keys that encrypt them.")
            .ValidateOnStart();

        services.AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<KimlikDbContext>()
                .ReplaceDefaultEntities<Guid>())
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetEndSessionEndpointUris("connect/endsession")
                    .SetIntrospectionEndpointUris("connect/introspect")
                    .SetRevocationEndpointUris("connect/revoke")
                    .SetDeviceAuthorizationEndpointUris("connect/device")
                    .SetEndUserVerificationEndpointUris("connect/verify");

                options.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow()
                    .AllowDeviceAuthorizationFlow();

                // PKCE for every client, confidential ones included (OAuth 2.0 Security BCP, RFC 9700).
                options.RequireProofKeyForCodeExchange();

                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess);

                // Access tokens are plain signed JWTs (RFC 9068) so any resource server can validate them.
                options.DisableAccessTokenEncryption();

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableEndUserVerificationEndpointPassthrough()
                    // Errors that cannot be returned to the client are rendered by the hosted pages.
                    .EnableErrorPassthrough();
            });

        services.AddSingleton<ConfigureOpenIddictServer>();
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>>(provider => provider.GetRequiredService<ConfigureOpenIddictServer>());
        services.AddSingleton<IConfigureOptions<OpenIddictServerAspNetCoreOptions>>(provider => provider.GetRequiredService<ConfigureOpenIddictServer>());

        services.AddScoped<OidcPrincipalFactory>();
        services.AddScoped<ScopeDescriptions>();

        // Must come after AddServer: see TokenKeyServiceCollectionExtensions.AddTokenKeys.
        services.AddTokenKeys(HealthProbeExtensions.ReadinessTag);

        return services;
    }
}
