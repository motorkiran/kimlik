using System.Security.Cryptography;
using Kimlik.Application.Accounts;
using Kimlik.Infrastructure.Security.TokenKeys;
using Kimlik.Server.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using OpenIddict.Client.WebIntegration;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Client.WebIntegration.OpenIddictClientWebIntegrationConstants;

namespace Kimlik.Server.SocialLogin;

internal static class SocialLoginServiceCollectionExtensions
{
    /// <summary>
    /// Sign-in with Google, Microsoft, Apple and GitHub, through OpenIddict's client, for the providers that
    /// <see cref="SocialLoginOptions"/> sets up, and with organizations' own providers (<see cref="SingleSignOn"/>).
    /// The providers can change at runtime (ADR 0002): the client's options, and so its registrations, are built again
    /// when their settings change.
    /// </summary>
    public static IServiceCollection AddSocialLogin(this IServiceCollection services)
    {
        services.AddOptions<SocialLoginOptions>()
            .BindConfiguration(SocialLoginOptions.SectionName)
            .Validate(
                options => !options.Apple.IsEnabled || ConfigureSocialProviders.HasAppleSigningKey(options.Apple),
                $"{SocialLoginOptions.SectionName}:Apple needs TeamId, KeyId and the PEM text of its private key as well as ClientId.")
            .ValidateOnStart();

        services.AddOpenIddict().AddClient(options =>
        {
            // The callbacks of every supported provider exist even before one is configured, so the client always
            // has the endpoint its flow needs; SSO connections share one.
            options.AllowAuthorizationCodeFlow()
                .SetRedirectionEndpointUris([.. Supported.Select(CallbackPath), SingleSignOn.CallbackPath]);

            // State tokens are protected with ASP.NET Core Data Protection, whose keys every instance shares; the
            // client still needs keys of its own, which come from the token keys (see AddTokenKeysToClient).
            options.UseDataProtection();
            options.UseSystemNetHttp().SetProductInformation(typeof(SocialLoginServiceCollectionExtensions).Assembly);
            options.UseAspNetCore()
                .EnableRedirectionEndpointPassthrough()
                .EnableErrorPassthrough()
                .DisableAutomaticAuthenticationSchemeForwarding();

            // The providers' own handlers and settings; ConfigureSocialProviders adds the registrations.
            options.UseWebProviders();
        });

        services.AddSingleton<IConfigureOptions<OpenIddictClientOptions>, ConfigureSocialProviders>();
        services.AddSingleton<IOptionsChangeTokenSource<OpenIddictClientOptions>>(provider =>
            new ConfigurationChangeTokenSource<OpenIddictClientOptions>(provider.GetRequiredService<IConfiguration>().GetSection(SocialLoginOptions.SectionName)));

        // SSO connections come from the database rather than the options.
        services.Replace(ServiceDescriptor.Singleton<OpenIddictClientService, SsoClientService>());
        services.AddSingleton<SamlServiceProvider>();
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

/// <summary>
/// Adds a client registration for each social provider its settings set up, whenever the client's options are built:
/// at startup, and again when the settings change at runtime. OpenIddict's web integration completes each from its
/// provider type, with the provider's endpoints and handlers. The settings are read anew rather than from their
/// monitor, whose cached value may not have caught up yet when the client's options are built again.
/// </summary>
internal sealed class ConfigureSocialProviders(IOptionsFactory<SocialLoginOptions> social) : IConfigureOptions<OpenIddictClientOptions>
{
    public void Configure(OpenIddictClientOptions options)
    {
        var settings = social.Create(Options.DefaultName);
        if (settings.Google.IsEnabled)
        {
            options.Registrations.Add(Registration(
                ExternalProviders.Google, ProviderTypes.Google, new OpenIddictClientWebIntegrationSettings.Google(), settings.Google, Scopes.Email, Scopes.Profile));
        }

        if (settings.Microsoft.IsEnabled)
        {
            options.Registrations.Add(Registration(
                ExternalProviders.Microsoft,
                ProviderTypes.Microsoft,
                new OpenIddictClientWebIntegrationSettings.Microsoft { Tenant = settings.Microsoft.Tenant },
                settings.Microsoft,
                Scopes.Email,
                Scopes.Profile));
        }

        if (settings.GitHub.IsEnabled)
        {
            options.Registrations.Add(Registration(
                ExternalProviders.GitHub, ProviderTypes.GitHub, new OpenIddictClientWebIntegrationSettings.GitHub(), settings.GitHub, "user:email"));
        }

        if (settings.Apple.IsEnabled && AppleSigningKey(settings.Apple) is { } key)
        {
            options.Registrations.Add(Registration(
                ExternalProviders.Apple,
                ProviderTypes.Apple,
                new OpenIddictClientWebIntegrationSettings.Apple { TeamId = settings.Apple.TeamId, SigningKey = key },
                settings.Apple,
                Scopes.Email,
                "name"));
        }
    }

    /// <summary>Whether Apple's settings make a key that Kimlik can sign its client secret with.</summary>
    public static bool HasAppleSigningKey(AppleProviderOptions options)
    {
        var key = AppleSigningKey(options);
        key?.ECDsa.Dispose();
        return key is not null;
    }

    private static OpenIddictClientRegistration Registration(
        string name, string type, object providerSettings, SocialProviderOptions options, params string[] scopes)
    {
        var registration = new OpenIddictClientRegistration
        {
            RegistrationId = name,
            ProviderName = name,
            ProviderType = type,
            ProviderSettings = providerSettings,
            ClientId = options.ClientId,
            ClientSecret = string.IsNullOrWhiteSpace(options.ClientSecret) ? null : options.ClientSecret,
            RedirectUri = new Uri(SocialLoginServiceCollectionExtensions.CallbackPath(name), UriKind.Relative),
        };

        registration.Scopes.UnionWith(scopes);
        registration.Properties[ExternalProviders.TrustEmailProperty] = options.TrustEmail;
        return registration;
    }

    /// <summary>
    /// The key that signs the client secret Kimlik makes for Apple, from the PEM text of the <c>.p8</c> key Apple issued;
    /// <see langword="null"/> when the settings lack it or it cannot be read.
    /// </summary>
    private static ECDsaSecurityKey? AppleSigningKey(AppleProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TeamId) || string.IsNullOrWhiteSpace(options.KeyId) || string.IsNullOrWhiteSpace(options.PrivateKey))
        {
            return null;
        }

        var algorithm = ECDsa.Create();
        try
        {
            // Pasted into a one-line field, the PEM text may have lost the line breaks around its label lines.
            var pem = options.PrivateKey
                .Replace("-----BEGIN PRIVATE KEY-----", "-----BEGIN PRIVATE KEY-----\n", StringComparison.Ordinal)
                .Replace("-----END PRIVATE KEY-----", "\n-----END PRIVATE KEY-----", StringComparison.Ordinal);
            algorithm.ImportFromPem(pem);
            return new ECDsaSecurityKey(algorithm) { KeyId = options.KeyId };
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            algorithm.Dispose();
            return null;
        }
    }
}
