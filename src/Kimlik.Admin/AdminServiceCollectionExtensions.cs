using Kimlik.Admin.Components;
using Kimlik.Admin.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Kimlik.Admin;

public static class AdminServiceCollectionExtensions
{
    /// <summary>
    /// The admin panel: Blazor in the Interactive Server render mode, behind the hosted pages' sign-in session and
    /// <see cref="AdminAccess.Policy"/>. It calls Application handlers in-process through <see cref="AdminOperations"/>.
    /// </summary>
    public static IServiceCollection AddKimlikAdmin(this IServiceCollection services)
    {
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddMudServices();

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, AdminAuthenticationStateProvider>();
        services.AddScoped<AdminSession>();
        services.AddScoped<AdminSessionLoader>();
        services.AddScoped<AdminCaller>();
        services.AddScoped<AdminOperations>();

        services.AddSingleton<IAuthorizationHandler, AdminAccessHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminAccess.Policy, policy => policy.RequireAuthenticatedUser().AddRequirements(new AdminAccessRequirement()));

        return services;
    }

    /// <summary>Serves the admin panel under <c>/admin</c>.</summary>
    public static RazorComponentsEndpointConventionBuilder MapKimlikAdmin(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapRazorComponents<App>()
            // The host's content security policy forbids framing already.
            .AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = null)
            .RequireAuthorization(AdminAccess.Policy);
}
