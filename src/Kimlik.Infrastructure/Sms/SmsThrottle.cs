using Kimlik.Application.Accounts;
using Microsoft.Extensions.Caching.Memory;

namespace Kimlik.Infrastructure.Sms;

/// <summary>
/// One text of each kind per account a minute, and five an hour, tracked in memory. With several instances each keeps
/// its own count, which still bounds the rate to a handful.
/// </summary>
internal sealed class SmsThrottle(IMemoryCache cache) : ISmsThrottle
{
    private const int HourlyLimit = 5;

    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private readonly Lock _lock = new();

    public bool TryAcquire(Guid userId, AccountSms kind)
    {
        var recent = $"sms:{userId:N}:{kind}";
        var hourly = $"sms:{userId:N}:hour";
        lock (_lock)
        {
            if (cache.TryGetValue(recent, out _) || (cache.TryGetValue(hourly, out Counter? counter) && counter!.Count >= HourlyLimit))
            {
                return false;
            }

            cache.Set(recent, true, Cooldown);
            if (counter is null)
            {
                cache.Set(hourly, new Counter { Count = 1 }, Hour);
            }
            else
            {
                counter.Count++;
            }

            return true;
        }
    }

    private sealed class Counter
    {
        public int Count { get; set; }
    }
}
