using Kimlik.Application.Accounts;
using Kimlik.Infrastructure.Clients;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Infrastructure.Security.TokenKeys;
using Kimlik.Server.Diagnostics;
using Kimlik.Server.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
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
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<KimlikDbContext>()
                    .ReplaceDefaultEntities<Guid>();

                // Previous client secrets keep working for a while after a rotation.
                options.UseKimlikApplicationManager();
            })
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("connect/authorize")
                    .SetPushedAuthorizationEndpointUris("connect/par")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetEndSessionEndpointUris("connect/endsession")
                    .SetIntrospectionEndpointUris("connect/introspect")
                    .SetRevocationEndpointUris("connect/revoke")
                    .SetDeviceAuthorizationEndpointUris("connect/device")
                    .SetEndUserVerificationEndpointUris("connect/verify");

                options.AllowAuthorizationCodeFlow()
                    .AllowTokenExchangeFlow()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow()
                    .AllowDeviceAuthorizationFlow();

                // PKCE is required client by client: of every browser and native app, and of web apps unless an administrator turned
                // it off (OAuth 2.0 Security BCP, RFC 9700). OpenIddict holds a client that sends it to it either way, with S256
                // only: plain gives an intercepted code to whoever saw the challenge.
                options.Configure(server => server.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));

                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Phone, Scopes.OfflineAccess);

                // Access tokens are plain signed JWTs (RFC 9068) so any resource server can validate them.
                options.DisableAccessTokenEncryption();

                // Web apps hear when sessions end, server to server, with the session's ID (OpenID Connect Back-Channel Logout), and
                // can ask for a multi-factor or phishing-resistant sign-in.
                options.AddEventHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>(handler => handler.UseInlineHandler(context =>
                {
                    context.Metadata["backchannel_logout_supported"] = true;
                    context.Metadata["backchannel_logout_session_supported"] = true;
                    context.Metadata["acr_values_supported"] = new OpenIddictParameter(System.Text.Json.JsonSerializer.SerializeToElement(AuthenticationContexts.Supported));
                    return default;
                }));

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
        services.AddSingleton<IBackChannelLogoutSender, BackChannelLogoutSender>();
        services.AddHttpClient(BackChannelLogoutSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            // A redirect would take the logout token, as a GET, to an address the client's administrators did not set.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        // Must come after AddServer: see TokenKeyServiceCollectionExtensions.AddTokenKeys.
        services.AddTokenKeys(HealthProbeExtensions.ReadinessTag);

        return services;
    }
}
