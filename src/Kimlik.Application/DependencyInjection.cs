using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Accounts;
using Kimlik.Application.Bootstrap;
using Kimlik.Application.Branding;
using Kimlik.Application.Organizations;
using Kimlik.Application.Plans;
using Kimlik.Application.Provisioning;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<AccountOptions>()
            .BindConfiguration(AccountOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.LockoutDuration > TimeSpan.Zero && options.SessionLifetime > TimeSpan.Zero, $"{AccountOptions.SectionName}: durations must be positive.")
            .ValidateOnStart();

        services.AddOptions<BrandingOptions>()
            .BindConfiguration(BrandingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OrganizationOptions>()
            .BindConfiguration(OrganizationOptions.SectionName)
            .Validate(options => options.InvitationLifetime > TimeSpan.Zero, $"{OrganizationOptions.SectionName}:InvitationLifetime must be positive.")
            .ValidateOnStart();

        services.AddOptions<PlanOptions>()
            .BindConfiguration(PlanOptions.SectionName)
            .Validate(options => options.ExpirationInterval > TimeSpan.Zero, $"{PlanOptions.SectionName}:ExpirationInterval must be positive.")
            .ValidateOnStart();

        services.AddOptions<BootstrapOptions>()
            .BindConfiguration(BootstrapOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.IsValid(), $"{BootstrapOptions.SectionName}: set both AdminEmail and AdminPassword, or neither.")
            .ValidateOnStart();

        services.AddScoped<AccessResolver>();
        services.AddScoped<AccessGuard>();
        services.AddScoped<UserOrganizations>();
        services.AddScoped<Entitlements>();
        services.AddScoped<OrganizationGuard>();
        services.AddScoped<MyOrganizations>();
        services.AddScoped<MyInvitations>();
        services.AddScoped<OrganizationSelfService>();
        services.AddScoped<PermissionProvisioner>();
        services.AddScoped<RoleProvisioner>();
        services.AddScoped<FeatureProvisioner>();
        services.AddScoped<PlanProvisioner>();
        services.AddScoped<ApiResourceProvisioner>();
        services.AddScoped<ClientProvisioner>();

        // Use case handlers are plain classes, one per use case, resolved directly by their callers;
        // outbox message handlers are also registered under their handler interface.
        foreach (var handler in typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true } && type.Name.EndsWith("Handler", StringComparison.Ordinal)))
        {
            services.AddScoped(handler);

            foreach (var contract in handler.GetInterfaces()
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IOutboxMessageHandler<>)))
            {
                services.AddScoped(contract, provider => provider.GetRequiredService(handler));
            }
        }

        return services;
    }
}
