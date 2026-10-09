using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Hosting;

internal static class RateLimitingServiceCollectionExtensions
{
    private static readonly PathString[] ProtocolEndpoints = ["/connect/token", "/connect/introspect", "/connect/revoke", "/connect/device"];

    public static IServiceCollection AddKimlikRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>()
            .BindConfiguration(RateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<RequestThrottle>();
        services.AddRateLimiter(_ => { });
        services.AddSingleton<IConfigureOptions<RateLimiterOptions>, ConfigureProtocolRateLimit>();

        return services;
    }

    /// <summary>
    /// Limits the protocol endpoints OpenIddict serves. The limiter runs before authentication, which is
    /// where OpenIddict authenticates clients, so rejected requests cost nothing.
    /// </summary>
    private sealed class ConfigureProtocolRateLimit(IOptions<RateLimitOptions> limits) : IConfigureOptions<RateLimiterOptions>
    {
        public void Configure(RateLimiterOptions options)
        {
            var permits = limits.Value.ProtocolRequestsPerMinute;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                Array.Exists(ProtocolEndpoints, endpoint => context.Request.Path.StartsWithSegments(endpoint))
                    ? RateLimitPartition.GetSlidingWindowLimiter(RequestThrottle.PartitionKey(context), _ => RequestThrottle.PerMinute(permits))
                    : RateLimitPartition.GetNoLimiter(string.Empty));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (rejection, cancellationToken) =>
            {
                var context = rejection.HttpContext;

                // Sliding windows do not report when a permit frees up; the whole window is a safe upper bound.
                var retryAfter = rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var reported) ? reported : TimeSpan.FromMinutes(1);
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Detail = "Too many requests. Slow down and try again." },
                });
            };
        }
    }
}
