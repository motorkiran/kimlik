using Kimlik.AspNetCore.Authorization;
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
    /// Accepts Kimlik access tokens for this API as the default authentication scheme, and enables
    /// <see cref="PermissionEndpointExtensions.RequirePermission{TBuilder}"/> and <see cref="RequirePermissionAttribute"/>.
    /// </summary>
    /// <returns>The authentication builder, to add more schemes.</returns>
    public static AuthenticationBuilder AddKimlik(this IServiceCollection services, Action<KimlikOptions> configure)
    {
        services.AddOptions<KimlikOptions>()
            .Configure(configure)
            .Validate(options => options.IsValid(), "Kimlik: Authority must be an absolute URL and Audience is required.")
            .ValidateOnStart();

        services.AddAuthorization();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, PermissionAuthorizationHandler>());
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureKimlikBearer>();

        return services.AddAuthentication(KimlikDefaults.AuthenticationScheme).AddJwtBearer(KimlikDefaults.AuthenticationScheme);
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
        }

        public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
    }
}
