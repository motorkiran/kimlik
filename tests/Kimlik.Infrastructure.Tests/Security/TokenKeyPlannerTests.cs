using Kimlik.Infrastructure.Security.TokenKeys;

namespace Kimlik.Infrastructure.Tests.Security;

public sealed class TokenKeyPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly TokenKeyOptions Options = new()
    {
        RotationInterval = TimeSpan.FromDays(90),
        PrepublishPeriod = TimeSpan.FromDays(2),
        RetentionPeriod = TimeSpan.FromDays(90),
    };

    [Fact]
    public void Plan_CreatesActiveKeysForEveryUse_OnFreshInstallation()
    {
        var plan = TokenKeyPlanner.Plan([], Now, Options);

        plan.KeysToCreate.ShouldBe(
        [
            new TokenKeySchedule(TokenKeyUse.Signing, Now, Now.AddDays(90), Now.AddDays(180)),
            new TokenKeySchedule(TokenKeyUse.Encryption, Now, Now.AddDays(90), Now.AddDays(180)),
        ]);
        plan.KeysToDelete.ShouldBeEmpty();
    }

    [Fact]
    public void Plan_ChangesNothing_WhileActiveKeysAreFarFromRetirement()
    {
        var keys = new[] { Key(TokenKeyUse.Signing, activatedDaysAgo: 10), Key(TokenKeyUse.Encryption, activatedDaysAgo: 10) };

        TokenKeyPlanner.Plan(keys, Now, Options).HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void Plan_SchedulesSuccessor_WhenActiveKeyRetiresWithinPrepublishPeriod()
    {
        var active = Key(TokenKeyUse.Signing, activatedDaysAgo: 89);

        var plan = TokenKeyPlanner.Plan([active, Key(TokenKeyUse.Encryption, activatedDaysAgo: 10)], Now, Options);

        plan.KeysToCreate.ShouldBe([new TokenKeySchedule(TokenKeyUse.Signing, active.RetiresAt, active.RetiresAt.AddDays(90), active.RetiresAt.AddDays(180))]);
    }

    [Fact]
    public void Plan_DoesNotScheduleSecondSuccessor()
    {
        var active = Key(TokenKeyUse.Signing, activatedDaysAgo: 89);
        var successor = Key(TokenKeyUse.Signing, activatedDaysAgo: -1);

        var plan = TokenKeyPlanner.Plan([active, successor, Key(TokenKeyUse.Encryption, activatedDaysAgo: 10)], Now, Options);

        plan.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void Plan_StartsNewActiveKey_WhenScheduleHasGap()
    {
        // Kimlik was offline when the successor was due: the only key retired a while ago.
        var retired = Key(TokenKeyUse.Signing, activatedDaysAgo: 100);

        var plan = TokenKeyPlanner.Plan([retired, Key(TokenKeyUse.Encryption, activatedDaysAgo: 10)], Now, Options);

        plan.KeysToCreate.ShouldBe([new TokenKeySchedule(TokenKeyUse.Signing, Now, Now.AddDays(90), Now.AddDays(180))]);
        plan.KeysToDelete.ShouldBeEmpty();
    }

    [Fact]
    public void Plan_DeletesExpiredKeys()
    {
        var expired = Key(TokenKeyUse.Signing, activatedDaysAgo: 200);
        var active = Key(TokenKeyUse.Signing, activatedDaysAgo: 10);

        var plan = TokenKeyPlanner.Plan([expired, active, Key(TokenKeyUse.Encryption, activatedDaysAgo: 10)], Now, Options);

        plan.KeysToDelete.ShouldBe([expired]);
        plan.KeysToCreate.ShouldBeEmpty();
    }

    private static TokenKey Key(TokenKeyUse use, int activatedDaysAgo)
    {
        var activatesAt = Now.AddDays(-activatedDaysAgo);
        return new TokenKey
        {
            KeyId = Guid.NewGuid().ToString("N"),
            Use = use,
            Algorithm = "test",
            ProtectedKey = [],
            CreatedAt = activatesAt,
            ActivatesAt = activatesAt,
            RetiresAt = activatesAt + Options.RotationInterval,
            ExpiresAt = activatesAt + Options.RotationInterval + Options.RetentionPeriod,
        };
    }
}
