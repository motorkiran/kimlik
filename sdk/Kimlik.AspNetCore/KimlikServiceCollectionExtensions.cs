using Kimlik.AspNetCore.ApiKeys;
using Kimlik.AspNetCore.Authorization;
using Kimlik.AspNetCore.Entitlements;
using Kimlik.Client;
using Kimlik.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kimlik.AspNetCore;

public static class KimlikServiceCollectionExtensions
{
    /// <summary>
    /// Accepts Kimlik access tokens for this API, and API keys if <see cref="KimlikOptions.ApiKeys"/> enables them, as
    /// the default authentication scheme, and enables <see cref="PermissionEndpointExtensions.RequirePermission{TBuilder}"/>
    /// and <see cref="RequirePermissionAttribute"/>.
    /// </summary>
    /// <returns>The authentication builder, to add more schemes.</returns>
    public static AuthenticationBuilder AddKimlik(this IServiceCollection services, Action<KimlikOptions> configure)
    {
        services.AddOptions<KimlikOptions>()
            .Configure(configure)
            .Validate(options => options.IsValid(), "Kimlik: Authority must be an absolute URL and Audience is required.")
            .Validate<IServiceProviderIsService>(
                (options, registered) => !options.ApiKeys.Enabled || registered.IsService(typeof(KimlikClient)),
                "Kimlik: API keys are verified through Kimlik.Client; register it with AddKimlikClient, as a service client holding kimlik.api_keys:verify.")
            .ValidateOnStart();

        services.AddAuthorization();
        services.TryAddSingleton<ApiKeyCache>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, PermissionAuthorizationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, FeatureAuthorizationHandler>());
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureKimlikBearer>();

        return services.AddAuthentication(KimlikDefaults.AuthenticationScheme)
            .AddJwtBearer(KimlikDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, KimlikApiKeyHandler>(KimlikDefaults.ApiKeyAuthenticationScheme, configureOptions: null);
    }

    /// <summary>
    /// Enables <see cref="IKimlikEntitlements"/> and <see cref="FeatureEndpointExtensions.RequireFeature{TBuilder}"/>.
    /// Plan definitions come through Kimlik.Client, so register it with <c>AddKimlikClient</c> as a service client
    /// holding <c>kimlik.plans:read</c>.
    /// </summary>
    public static IServiceCollection AddKimlikEntitlements(this IServiceCollection services, Action<KimlikEntitlementsOptions>? configure = null)
    {
        var options = services.AddOptions<KimlikEntitlementsOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IKimlikEntitlements, KimlikEntitlements>();
        return services;
    }

    private sealed class ConfigureKimlikBearer(IOptions<KimlikOptions> kimlik) : IConfigureNamedOptions<JwtBearerOptions>
    {
        public void Configure(string? name, JwtBearerOptions options)
        {
            if (name != KimlikDefaults.AuthenticationScheme)
            {
                return;
            }

            options.Authority = kimlik.Value.Authority!.AbsoluteUri;
            options.Audience = kimlik.Value.Audience;
            options.RequireHttpsMetadata = kimlik.Value.RequireHttpsMetadata;

            // Claims keep the names they have in the token, such as "sub", "roles" and "permissions".
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = JwtRegisteredClaimNames.Name;
            options.TokenValidationParameters.RoleClaimType = KimlikClaimTypes.Roles;

            // Only access tokens (RFC 9068): an ID token issued to some client must not open the API.
            options.TokenValidationParameters.ValidTypes = ["at+jwt"];

            // API keys go to their own handler, which asks Kimlik about them.
            options.ForwardDefaultSelector = context =>
                kimlik.Value.ApiKeys.Enabled && KimlikApiKeyHandler.ApiKeyOf(context.Request) is not null ? KimlikDefaults.ApiKeyAuthenticationScheme : null;
        }

        public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
    }
}
