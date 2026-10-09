using System.Globalization;
using System.Security.Claims;
using Kimlik.Admin.Security;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Application.Mfa;
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

        // Sessions notice a changed security stamp (password reset, suspension) within this interval.
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.FromMinutes(5);
            options.OnRefreshingPrincipal = KeepHowTheUserSignedIn;
        });

        services.AddHttpContextAccessor();
        // The caller is the HTTP request's, except in an admin panel operation, which sets its administrator.
        services.AddScoped<HttpRequestContext>();
        services.AddScoped<IRequestContext>(provider =>
            provider.GetService<AdminCaller>() is { IsSet: true } administrator ? administrator : provider.GetRequiredService<HttpRequestContext>());
        services.AddScoped<PasswordHashTiming>();
        services.AddScoped<SignOutService>();
        services.AddScoped<SignInFlow>();
        services.AddSingleton<IConfigureOptions<IdentityPasskeyOptions>, ConfigurePasskeys>();
        services.AddScoped<PasskeyCeremonies>();
        services.AddSingleton<PendingEmailCode>();
        services.AddScoped<AccountErrorMessages>();
        services.AddSingleton<IAccountLinks, AccountLinks>();

        return services;
    }

    /// <summary>
    /// The security stamp check rebuilds the session's claims from the user, which would drop how, when and by whom they
    /// were signed in (<see cref="SignInFlow.KeptClaims"/>).
    /// </summary>
    private static Task KeepHowTheUserSignedIn(SecurityStampRefreshingPrincipalContext context)
    {
        if (context.NewPrincipal?.Identity is ClaimsIdentity identity && context.CurrentPrincipal is { } current)
        {
            identity.AddClaims(SignInFlow.KeptClaims(current));
        }

        return Task.CompletedTask;
    }

    private sealed class ConfigureSessionCookie(IOptions<ServerOptions> server, IOptions<AccountOptions> accounts, IOptions<MfaOptions> mfa, TimeProvider timeProvider)
        : IConfigureNamedOptions<CookieAuthenticationOptions>
    {
        public void Configure(string? name, CookieAuthenticationOptions options)
        {
            if (name == IdentityConstants.ApplicationScheme)
            {
                Secure(options, "session");
                options.ExpireTimeSpan = accounts.Value.SessionLifetime;
                options.SlidingExpiration = true;
                options.LoginPath = "/signin";
                options.LogoutPath = "/signout";
                options.AccessDeniedPath = "/error";

                // Every new session records when the user signed in; renewals do not sign in again, so they keep it.
                var signingIn = options.Events.OnSigningIn;
                options.Events.OnSigningIn = context =>
                {
                    if (context.Principal?.Identity is ClaimsIdentity identity && !identity.HasClaim(claim => claim.Type == SignInFlow.SignedInAtClaim))
                    {
                        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                        identity.AddClaim(new Claim(SignInFlow.SignedInAtClaim, now, ClaimValueTypes.Integer64));
                    }

                    return signingIn(context);
                };
            }
            else if (name == IdentityConstants.TwoFactorUserIdScheme)
            {
                // A sign-in waiting for its second factor.
                Secure(options, "mfa-pending");
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = false;
            }
            else if (name == IdentityConstants.ExternalScheme)
            {
                // An account at another provider, waiting to be linked once the user signs in.
                Secure(options, "external");
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                options.SlidingExpiration = false;
            }
            else if (name == IdentityConstants.TwoFactorRememberMeScheme)
            {
                // A browser the user trusts to skip the second factor.
                Secure(options, "mfa-trusted");
                options.ExpireTimeSpan = mfa.Value.RememberBrowserFor;
                options.SlidingExpiration = false;
            }
        }

        public void Configure(CookieAuthenticationOptions options) => Configure(Options.DefaultName, options);

        /// <summary>The __Host- prefix pins a cookie to this host and path and requires HTTPS.</summary>
        private void Secure(CookieAuthenticationOptions options, string name)
        {
            options.Cookie.Name = server.Value.RequireHttps ? $"__Host-kimlik.{name}" : $"kimlik.{name}";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = server.Value.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        }
    }
}
