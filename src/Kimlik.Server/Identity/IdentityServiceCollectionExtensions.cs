using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Identity;

internal static class IdentityServiceCollectionExtensions
{
    /// <summary>The sign-in session of the hosted pages: Identity's cookies plus the sign-in manager.</summary>
    public static IServiceCollection AddSignInSession(this IServiceCollection services)
    {
        services.AddIdentityCore<User>().AddSignInManager();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.AddSingleton<IConfigureOptions<CookieAuthenticationOptions>, ConfigureSessionCookie>();

        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext, HttpRequestContext>();
        services.AddScoped<PasswordHashTiming>();
        services.AddScoped<SignOutService>();
        services.AddScoped<AccountErrorMessages>();

        return services;
    }

    private sealed class ConfigureSessionCookie(IOptions<ServerOptions> server, IOptions<AccountOptions> accounts)
        : IConfigureNamedOptions<CookieAuthenticationOptions>
    {
        public void Configure(string? name, CookieAuthenticationOptions options)
        {
            if (name != IdentityConstants.ApplicationScheme)
            {
                return;
            }

            // The __Host- prefix pins the cookie to this host and path and requires HTTPS.
            options.Cookie.Name = server.Value.RequireHttps ? "__Host-kimlik.session" : "kimlik.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = server.Value.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = accounts.Value.SessionLifetime;
            options.SlidingExpiration = true;
            options.LoginPath = "/signin";
            options.LogoutPath = "/signout";
            options.AccessDeniedPath = "/error";
        }

        public void Configure(CookieAuthenticationOptions options) => Configure(Options.DefaultName, options);
    }
}
