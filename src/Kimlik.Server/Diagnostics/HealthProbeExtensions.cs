using Kimlik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Kimlik.Server.Diagnostics;

/// <summary>
/// Liveness and readiness probes for orchestrators and load balancers.
/// </summary>
internal static class HealthProbeExtensions
{
    /// <summary>Checks with this tag must pass before the instance receives traffic.</summary>
    public const string ReadinessTag = "ready";

    public static IServiceCollection AddHealthProbes(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<KimlikDbContext>("database", tags: [ReadinessTag]);

        return services;
    }

    /// <summary>
    /// Serves the probes as middleware placed before authentication, so they answer even while the instance
    /// cannot serve anything else (for example before its token keys are loaded).
    /// </summary>
    public static IApplicationBuilder UseHealthProbes(this IApplicationBuilder app)
    {
        // Liveness only reports that the process is up, so a database outage
        // makes Kimlik unready instead of getting it restarted.
        app.UseHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.UseHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadinessTag) });

        return app;
    }
}
