namespace Kimlik.Domain.Plans;

/// <summary>How much of a metered limit a subscriber used in a calendar month, in UTC.</summary>
public sealed class UsageCounter
{
    // Used by EF Core.
    private UsageCounter()
    {
    }

    public Guid Id { get; private init; }

    public Guid? UserId { get; private init; }

    public Guid? OrganizationId { get; private init; }

    public Guid FeatureId { get; private init; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Period { get; private init; }

    public long Used { get; private init; }

    public DateTimeOffset UpdatedAt { get; private init; }

    /// <summary>The first day of the month that <paramref name="now"/> is in, in UTC.</summary>
    public static DateOnly PeriodOf(DateTimeOffset now) => new(now.UtcDateTime.Year, now.UtcDateTime.Month, 1);
}

/// <summary>An idempotency key a backend sent with use, so that a retry counts once; kept for a day.</summary>
public sealed class UsageRecord
{
    public const int KeyMaxLength = 100;

    // Used by EF Core.
    private UsageRecord()
    {
    }

    public Guid Id { get; private init; }

    public Guid? UserId { get; private init; }

    public Guid? OrganizationId { get; private init; }

    public string Key { get; private init; } = null!;

    public DateTimeOffset CreatedAt { get; private init; }
}
