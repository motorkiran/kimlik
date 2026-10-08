namespace Kimlik.Infrastructure.Security.TokenKeys;

internal sealed record TokenKeySchedule(TokenKeyUse Use, DateTimeOffset ActivatesAt, DateTimeOffset RetiresAt, DateTimeOffset ExpiresAt);

internal sealed record TokenKeyPlan(IReadOnlyList<TokenKeySchedule> KeysToCreate, IReadOnlyList<TokenKey> KeysToDelete)
{
    public bool HasChanges => KeysToCreate.Count > 0 || KeysToDelete.Count > 0;
}

/// <summary>
/// Decides which token keys to create and delete. For every key use there is always exactly one active key;
/// its successor is created ahead of time and published before it activates, and expired keys are deleted.
/// </summary>
internal static class TokenKeyPlanner
{
    public static TokenKeyPlan Plan(IReadOnlyCollection<TokenKey> keys, DateTimeOffset now, TokenKeyOptions options)
    {
        var keysToCreate = new List<TokenKeySchedule>();

        foreach (var use in Enum.GetValues<TokenKeyUse>())
        {
            var usableKeys = keys.Where(key => key.Use == use && key.ExpiresAt > now).ToList();

            // A missing active key means a fresh installation or a gap in the schedule
            // (Kimlik was offline when the successor was due); either way, start one now.
            var activeKeyRetiresAt = usableKeys.Where(key => IsActive(key, now)).MaxBy(key => key.ActivatesAt)?.RetiresAt;
            if (activeKeyRetiresAt is null)
            {
                var replacement = Schedule(use, now, options);
                keysToCreate.Add(replacement);
                activeKeyRetiresAt = replacement.RetiresAt;
            }

            var hasSuccessor = usableKeys.Exists(key => key.ActivatesAt > now);
            if (!hasSuccessor && activeKeyRetiresAt.Value - now <= options.PrepublishPeriod)
            {
                keysToCreate.Add(Schedule(use, activeKeyRetiresAt.Value, options));
            }
        }

        var keysToDelete = keys.Where(key => key.ExpiresAt <= now).ToList();

        return new TokenKeyPlan(keysToCreate, keysToDelete);
    }

    public static bool IsActive(TokenKey key, DateTimeOffset now) => key.ActivatesAt <= now && now < key.RetiresAt;

    private static TokenKeySchedule Schedule(TokenKeyUse use, DateTimeOffset activatesAt, TokenKeyOptions options)
    {
        var retiresAt = activatesAt + options.RotationInterval;
        return new TokenKeySchedule(use, activatesAt, retiresAt, retiresAt + options.RetentionPeriod);
    }
}
