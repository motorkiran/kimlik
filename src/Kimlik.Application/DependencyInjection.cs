using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Accounts;
using Kimlik.Application.Branding;
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

        services.AddScoped<AccessResolver>();

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
