using Kimlik.Application.Auditing;
using Kimlik.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Auditing;

public sealed class AuditRetentionTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EventsPastTheRetentionPeriod_AreDeleted()
    {
        var expired = await RecordAsync(DateTimeOffset.UtcNow - TimeSpan.FromDays(366));
        var kept = await RecordAsync(DateTimeOffset.UtcNow - TimeSpan.FromDays(364));

        await server.WithServicesAsync(services => services.GetRequiredService<DeleteOldAuditEventsHandler>().HandleAsync(CancellationToken));

        var remaining = await server.QueryDatabaseAsync(context =>
            context.AuditEvents.Where(auditEvent => auditEvent.Id == expired || auditEvent.Id == kept).Select(auditEvent => auditEvent.Id).ToListAsync(CancellationToken));
        remaining.ShouldBe([kept]);
    }

    private Task<Guid> RecordAsync(DateTimeOffset occurredAt) =>
        server.QueryDatabaseAsync(async context =>
        {
            var auditEvent = new AuditEvent { OccurredAt = occurredAt, Action = AuditActions.UserSignedIn, ActorType = AuditActorType.System };
            context.Add(auditEvent);
            await context.SaveChangesAsync(CancellationToken);
            return auditEvent.Id;
        });
}
