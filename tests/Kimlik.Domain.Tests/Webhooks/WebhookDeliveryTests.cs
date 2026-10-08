using Kimlik.Domain.Webhooks;

namespace Kimlik.Domain.Tests.Webhooks;

public sealed class WebhookDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AbandonedAttempt_IsMadeAgainAtOnce_WithoutCounting()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), "test", "{}", Now);
        delivery.BeginAttempt(Now, leaseUntil: Now.AddMinutes(2));

        delivery.AbandonAttempt(Now.AddSeconds(5));

        delivery.Attempts.ShouldBe(0);
        delivery.Status.ShouldBe(WebhookDeliveryStatus.Pending);
        delivery.NextAttemptAt.ShouldBe(Now.AddSeconds(5));
    }
}
