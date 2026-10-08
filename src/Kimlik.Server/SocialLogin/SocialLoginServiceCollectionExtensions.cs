using System.Security.Cryptography;
using Kimlik.Infrastructure.Security.TokenKeys;
using Kimlik.Server.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.SocialLogin;

internal static class SocialLoginServiceCollectionExtensions
{
    /// <summary>
    /// Sign-in with Google, Microsoft, Apple and GitHub, through OpenIddict's client, for the providers that
    /// <see cref="SocialLoginOptions"/> configures.
    /// </summary>
    public static IServiceCollection AddSocialLogin(this IServiceCollection services, IConfiguration configuration)
    {
        var social = configuration.GetSection(SocialLoginOptions.SectionName).Get<SocialLoginOptions>() ?? new SocialLoginOptions();

        services.AddOpenIddict().AddClient(options =>
        {
            // The callbacks of every supported provider exist even before one is configured, so the client always
            // has the endpoint its flow needs.
            options.AllowAuthorizationCodeFlow()
                .SetRedirectionEndpointUris([.. Supported.Select(CallbackPath)]);

            // State tokens are protected with ASP.NET Core Data Protection, whose keys every instance shares; the
            // client still needs keys of its own, which come from the token keys (see AddTokenKeysToClient).
            options.UseDataProtection();
            options.UseSystemNetHttp().SetProductInformation(typeof(SocialLoginServiceCollectionExtensions).Assembly);
            options.UseAspNetCore()
                .EnableRedirectionEndpointPassthrough()
                .EnableErrorPassthrough()
                .DisableAutomaticAuthenticationSchemeForwarding();

            var providers = options.UseWebProviders();

            if (social.Google.IsEnabled)
            {
                providers.AddGoogle(google => Trust(
                    google.SetRegistrationId(ExternalProviders.Google)
                        .SetProviderName(ExternalProviders.Google)
                        .SetClientId(social.Google.ClientId!)
                        .SetClientSecret(social.Google.ClientSecret!)
                        .SetRedirectUri(CallbackPath(ExternalProviders.Google))
                        .AddScopes(Scopes.Email, Scopes.Profile)
                        .Registration,
                    social.Google));
            }

            if (social.Microsoft.IsEnabled)
            {
                providers.AddMicrosoft(microsoft => Trust(
                    microsoft.SetRegistrationId(ExternalProviders.Microsoft)
                        .SetProviderName(ExternalProviders.Microsoft)
                        .SetClientId(social.Microsoft.ClientId!)
                        .SetClientSecret(social.Microsoft.ClientSecret!)
                        .SetTenant(social.Microsoft.Tenant)
                        .SetRedirectUri(CallbackPath(ExternalProviders.Microsoft))
                        .AddScopes(Scopes.Email, Scopes.Profile)
                        .Registration,
                    social.Microsoft));
            }

            if (social.GitHub.IsEnabled)
            {
                providers.AddGitHub(gitHub => Trust(
                    gitHub.SetRegistrationId(ExternalProviders.GitHub)
                        .SetProviderName(ExternalProviders.GitHub)
                        .SetClientId(social.GitHub.ClientId!)
                        .SetClientSecret(social.GitHub.ClientSecret!)
                        .SetRedirectUri(CallbackPath(ExternalProviders.GitHub))
                        .AddScopes("user:email")
                        .Registration,
                    social.GitHub));
            }

            if (social.Apple.IsEnabled)
            {
                providers.AddApple(apple => Trust(
                    apple.SetRegistrationId(ExternalProviders.Apple)
                        .SetProviderName(ExternalProviders.Apple)
                        .SetClientId(social.Apple.ClientId!)
                        .SetTeamId(social.Apple.TeamId!)
                        .SetSigningKey(AppleSigningKey(social.Apple))
                        .SetRedirectUri(CallbackPath(ExternalProviders.Apple))
                        .AddScopes(Scopes.Email, "name")
                        .Registration,
                    social.Apple));
            }
        });

        services.AddTokenKeysToClient();
        services.AddSingleton<IConfigureOptions<OpenIddictClientAspNetCoreOptions>, ConfigureTransportSecurity>();
        services.AddSingleton<ExternalProviders>();
        services.AddScoped<PendingLinks>();
        services.AddScoped<GitHubEmails>();
        services.AddHttpClient(GitHubEmails.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Kimlik");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });

        return services;
    }

    private static readonly string[] Supported = [ExternalProviders.Google, ExternalProviders.Microsoft, ExternalProviders.Apple, ExternalProviders.GitHub];

    /// <summary>Where a provider sends people back, relative to the installation's URL.</summary>
    public static string CallbackPath(string provider) => $"signin/external/callback/{provider}";

    private static void Trust(OpenIddictClientRegistration registration, SocialProviderOptions options) =>
        registration.Properties[ExternalProviders.TrustEmailProperty] = options.TrustEmail;

    private static ECDsaSecurityKey AppleSigningKey(AppleProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TeamId) || string.IsNullOrWhiteSpace(options.KeyId) || string.IsNullOrWhiteSpace(options.PrivateKey))
        {
            throw new InvalidOperationException($"{SocialLoginOptions.SectionName}:Apple needs TeamId, KeyId and PrivateKey as well as ClientId.");
        }

        var algorithm = ECDsa.Create();
        algorithm.ImportFromPem(options.PrivateKey);
        return new ECDsaSecurityKey(algorithm) { KeyId = options.KeyId };
    }

    private sealed class ConfigureTransportSecurity(IOptions<ServerOptions> server) : IConfigureOptions<OpenIddictClientAspNetCoreOptions>
    {
        public void Configure(OpenIddictClientAspNetCoreOptions options)
        {
            if (server.Value.RequireHttps)
            {
                return;
            }

            // The cookie that binds a provider's response to the browser that went there is SameSite=None, so that
            // responses posted back (Apple's) carry it, which requires HTTPS. Over plain HTTP, in development, it is
            // Lax instead: providers that redirect back still carry it.
            options.DisableTransportSecurityRequirement = true;
            options.CookieBuilder.SameSite = SameSiteMode.Lax;
            options.CookieBuilder.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        }
    }
}
