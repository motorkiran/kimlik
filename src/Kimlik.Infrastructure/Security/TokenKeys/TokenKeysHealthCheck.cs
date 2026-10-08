using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>Reports the instance as not ready until it has keys to sign and encrypt tokens.</summary>
internal sealed class TokenKeysHealthCheck(ITokenKeyStatus status) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(status.IsLoaded
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The token keys have not been loaded yet."));
}
