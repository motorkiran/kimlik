using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>
/// Loads the token keys at startup and keeps them current. Kimlik starts even when the database is not
/// reachable yet; until the first refresh succeeds the readiness probe reports the instance as not ready.
/// </summary>
internal sealed partial class TokenKeyRefreshService(
    TokenKeyRefresher refresher,
    IOptions<TokenKeyOptions> options,
    TimeProvider timeProvider,
    ILogger<TokenKeyRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = FirstRetryDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                if (await refresher.RefreshAsync(stoppingToken))
                {
                    LogKeysChanged(logger);
                }

                delay = options.Value.RefreshInterval;
                retryDelay = FirstRetryDelay;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Keep trying: the database may simply not be reachable yet.
                LogRefreshFailed(logger, exception, retryDelay);
                delay = retryDelay;
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
            }

            await Task.Delay(delay, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Token keys changed; the OpenID Connect server now uses the new set")]
    private static partial void LogKeysChanged(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Refreshing the token keys failed; retrying in {RetryDelay}")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, TimeSpan retryDelay);
}
