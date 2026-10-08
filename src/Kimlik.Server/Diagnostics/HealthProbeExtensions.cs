using Kimlik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Kimlik.Server.Diagnostics;

/// <summary>
/// Liveness and readiness probes for orchestrators and load balancers.
/// </summary>
internal static class HealthProbeExtensions
{
    private const string ReadinessTag = "ready";

    public static IServiceCollection AddHealthProbes(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<KimlikDbContext>("database", tags: [ReadinessTag]);

        return services;
    }

    public static IEndpointRouteBuilder MapHealthProbes(this IEndpointRouteBuilder endpoints)
    {
        // Liveness only reports that the process is up, so a database outage
        // makes Kimlik unready instead of getting it restarted.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .DisableHttpMetrics();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadinessTag) })
            .DisableHttpMetrics();

        return endpoints;
    }
}
