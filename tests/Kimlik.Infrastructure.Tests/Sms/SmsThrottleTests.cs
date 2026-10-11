using Kimlik.Application.Accounts;
using Kimlik.Infrastructure.Sms;
using Microsoft.Extensions.Caching.Memory;

namespace Kimlik.Infrastructure.Tests.Sms;

public sealed class SmsThrottleTests
{
    private const string Number = "+905321234567";

    [Fact]
    public void Account_WaitsAMinuteBetweenTextsOfAKind()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var throttle = new SmsThrottle(cache);
        var user = Guid.NewGuid();

        throttle.TryAcquire(user, AccountSms.PhoneVerification, Number).ShouldBeTrue();
        throttle.TryAcquire(user, AccountSms.PhoneVerification, "+905321234568").ShouldBeFalse();
        throttle.TryAcquire(user, AccountSms.SignInCode, Number).ShouldBeTrue("each kind has a budget of its own");
    }

    [Fact]
    public void Number_GetsAFewTextsOfAKindAnHour_WhicheverAccountsAsk()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var throttle = new SmsThrottle(cache);

        for (var account = 0; account < 5; account++)
        {
            throttle.TryAcquire(Guid.NewGuid(), AccountSms.PhoneVerification, Number).ShouldBeTrue();
        }

        throttle.TryAcquire(Guid.NewGuid(), AccountSms.PhoneVerification, Number).ShouldBeFalse();
        throttle.TryAcquire(Guid.NewGuid(), AccountSms.SignInCode, Number).ShouldBeTrue();
    }
}
