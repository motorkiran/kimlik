using Kimlik.Application.Accounts;
using Microsoft.Extensions.Caching.Memory;

namespace Kimlik.Infrastructure.Email;

/// <summary>
/// One email of each kind per account per minute, tracked in memory. With several instances each one keeps
/// its own count, which still bounds the rate to a handful per minute.
/// </summary>
internal sealed class AccountEmailThrottle(IMemoryCache cache) : IAccountEmailThrottle
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    public bool TryAcquire(Guid userId, AccountEmail kind)
    {
        var key = $"account-email:{userId:N}:{kind}";
        if (cache.TryGetValue(key, out _))
        {
            return false;
        }

        cache.Set(key, true, Cooldown);
        return true;
    }
}
