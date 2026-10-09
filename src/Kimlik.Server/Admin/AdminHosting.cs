using Kimlik.Admin;
using Kimlik.Admin.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Kimlik.Server.Admin;

/// <summary>
/// Hosts the admin panel in this process, unless <c>Kimlik:Admin:Enabled</c> turns it off for the instance: its
/// services are always there, its pages only when enabled.
/// </summary>
internal static class AdminHosting
{
    public static bool IsAdminPanelEnabled(this IConfiguration configuration) => configuration.GetValue("Kimlik:Admin:Enabled", defaultValue: true);

    public static IServiceCollection AddAdminPanel(this IServiceCollection services)
    {
        services.AddKimlikAdmin();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AdminAuthorizationResultHandler>();
        return services;
    }

    /// <summary>
    /// The admin panel loads Blazor's and MudBlazor's scripts and keeps inline styles, which the hosted pages' policy
    /// forbids, so its pages get a policy of their own.
    /// </summary>
    public static IApplicationBuilder UseAdminContentSecurityPolicy(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/admin"))
        {
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; "
                + "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        }

        return next(context);
    });

    /// <summary>
    /// Sends an administrator whose session lacks the second factor the panel requires to add it, and anyone else the
    /// panel turns away to a page that says so.
    /// </summary>
    private sealed class AdminAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();

        public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Forbidden && policy.Requirements.OfType<AdminAccessRequirement>().Any())
            {
                var needsSecondFactor = authorizeResult.AuthorizationFailure?.FailureReasons.Any(reason => reason.Message == AdminAccess.SecondFactorNeeded) == true;
                var returnUrl = Uri.EscapeDataString($"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}");
                context.Response.Redirect(needsSecondFactor ? $"{context.Request.PathBase}/signin/step-up?returnUrl={returnUrl}" : $"{context.Request.PathBase}/admin/denied");
                return Task.CompletedTask;
            }

            return _default.HandleAsync(next, context, policy, authorizeResult);
        }
    }
}
