using System.Globalization;
using Kimlik.Infrastructure.Security.TokenKeys;

namespace Kimlik.Server.Diagnostics;

internal static class ReadinessGateExtensions
{
    private const int RetryAfterSeconds = 5;

    /// <summary>
    /// Answers <c>503 Service Unavailable</c> until the instance has its token keys, instead of failing
    /// every request while it is still starting or waiting for the database.
    /// </summary>
    public static IApplicationBuilder UseReadinessGate(this IApplicationBuilder app)
    {
        var status = app.ApplicationServices.GetRequiredService<ITokenKeyStatus>();

        return app.Use(async (context, next) =>
        {
            if (status.IsLoaded)
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = { Status = StatusCodes.Status503ServiceUnavailable, Detail = "Kimlik is starting and not ready to serve requests yet." },
            });
        });
    }
}
