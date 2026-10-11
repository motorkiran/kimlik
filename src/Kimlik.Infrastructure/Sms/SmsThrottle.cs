using Kimlik.Application.Accounts;
using Microsoft.Extensions.Caching.Memory;

namespace Kimlik.Infrastructure.Sms;

/// <summary>
/// For each account and kind of text, one a minute and five an hour; for each number and kind, five an hour, whichever
/// accounts ask, without the wait, so that a number moved to another account is not held up. Tracked in memory: with
/// several instances each keeps its own count, which still bounds the rate to a handful.
/// </summary>
internal sealed class SmsThrottle(IMemoryCache cache) : ISmsThrottle
{
    private const int HourlyLimit = 5;

    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private readonly Lock _lock = new();

    public bool TryAcquire(Guid userId, AccountSms kind, string phoneNumber)
    {
        var account = $"sms:account:{userId:N}:{kind}";
        var number = $"sms:number:{phoneNumber}:{kind}";
        lock (_lock)
        {
            if (cache.TryGetValue($"{account}:recent", out _) || Spent(account) || Spent(number))
            {
                return false;
            }

            cache.Set($"{account}:recent", true, Cooldown);
            Count(account);
            Count(number);
            return true;
        }
    }

    private bool Spent(string budget) => cache.TryGetValue(budget, out Counter? counter) && counter!.Count >= HourlyLimit;

    private void Count(string budget)
    {
        if (cache.TryGetValue(budget, out Counter? counter))
        {
            counter!.Count++;
        }
        else
        {
            cache.Set(budget, new Counter { Count = 1 }, Hour);
        }
    }

    private sealed class Counter
    {
        public int Count { get; set; }
    }
}
