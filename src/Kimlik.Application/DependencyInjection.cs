using Kimlik.Application.Accounts;
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

        // Use case handlers are plain classes, one per use case, resolved directly by their callers.
        foreach (var handler in typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true } && type.Name.EndsWith("Handler", StringComparison.Ordinal)))
        {
            services.AddScoped(handler);
        }

        return services;
    }
}
