using Kimlik.Domain.Plans;

namespace Kimlik.Domain.Tests.Plans;

public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly Subscriber User = Subscriber.User(Guid.CreateVersion7());

    [Fact]
    public void Trial_EndsWhenTheTrialEnds()
    {
        var subscription = Subscription.Create(User, Guid.CreateVersion7(), trialEndsAt: Now.AddDays(14), currentPeriodEnd: null, null, Now).Value;

        subscription.Status.ShouldBe(SubscriptionStatus.Trialing);
        subscription.IsInEffectAt(Now.AddDays(13)).ShouldBeTrue();
        subscription.ExpireIfEnded(Now.AddDays(13)).ShouldBeFalse();
        subscription.ExpireIfEnded(Now.AddDays(14)).ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Expired);
    }

    [Fact]
    public void Canceled_StaysInEffectUntilThePeriodEnds_UnlessResumed()
    {
        var subscription = Subscription.Create(User, Guid.CreateVersion7(), null, currentPeriodEnd: Now.AddDays(30), "sub_123", Now).Value;

        subscription.Cancel(Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Canceled);
        subscription.IsInEffectAt(Now.AddDays(29)).ShouldBeTrue();

        subscription.Update(null, Now.AddDays(60), "sub_123", SubscriptionStatus.Active, Now.AddDays(2)).IsSuccess.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
        subscription.CanceledAt.ShouldBeNull();
    }

    [Fact]
    public void Canceling_ASubscriptionWithoutAnEnd_EndsItAtOnce()
    {
        var subscription = Subscription.Create(User, Guid.CreateVersion7(), null, null, null, Now).Value;

        subscription.Cancel(Now.AddDays(1)).IsSuccess.ShouldBeTrue();

        subscription.Status.ShouldBe(SubscriptionStatus.Expired);
        subscription.Update(null, null, null, SubscriptionStatus.Active, Now.AddDays(2)).Error.ShouldBe(PlanErrors.SubscriptionEnded);
    }

    [Fact]
    public void Period_MustEndAfterItStarts()
    {
        Subscription.Create(User, Guid.CreateVersion7(), null, currentPeriodEnd: Now.AddDays(-1), null, Now).Error.ShouldBe(PlanErrors.InvalidSubscription);
    }
}
