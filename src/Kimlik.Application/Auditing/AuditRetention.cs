using Kimlik.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Auditing;

/// <summary>From the <c>Kimlik:Audit</c> configuration section.</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Kimlik:Audit";

    /// <summary>How long audit events are kept.</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(365);
}

/// <summary>Deletes a batch of the audit events that are older than the retention period, oldest first.</summary>
public sealed class DeleteOldAuditEventsHandler(IKimlikDbContext context, IOptions<AuditOptions> options, TimeProvider timeProvider)
{
    public const int BatchSize = 1000;

    /// <summary>Returns how many events were deleted; fewer than <see cref="BatchSize"/> means none are left.</summary>
    public Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - options.Value.RetentionPeriod;

        return context.AuditEvents
            .Where(auditEvent => auditEvent.OccurredAt < cutoff)
            .OrderBy(auditEvent => auditEvent.OccurredAt)
            .Take(BatchSize)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
