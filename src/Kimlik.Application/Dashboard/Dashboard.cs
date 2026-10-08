using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using DomainUserStatus = Kimlik.Domain.Users.UserStatus;

namespace Kimlik.Application.Dashboard;

/// <summary>The installation at a glance, for the admin panel's home page.</summary>
public sealed record DashboardResponse(
    int Users,
    int SuspendedUsers,
    int SignInsToday,
    int Organizations,
    int Clients,
    int CurrentSubscriptions,
    int FailingWebhookEndpoints);

public sealed class GetDashboardHandler(IKimlikDbContext context, TimeProvider timeProvider)
{
    public async Task<DashboardResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var dayAgo = timeProvider.GetUtcNow().AddDays(-1);

        return new DashboardResponse(
            await context.Users.CountAsync(cancellationToken),
            await context.Users.CountAsync(user => user.Status == DomainUserStatus.Suspended, cancellationToken),
            await context.AuditEvents.CountAsync(auditEvent => auditEvent.Action == AuditActions.UserSignedIn && auditEvent.OccurredAt >= dayAgo, cancellationToken),
            await context.Organizations.CountAsync(cancellationToken),
            await context.Applications.CountAsync(cancellationToken),
            await context.Subscriptions.CountAsync(subscription => subscription.Status != SubscriptionStatus.Expired, cancellationToken),
            await context.WebhookEndpoints.CountAsync(endpoint => endpoint.FailingSince != null, cancellationToken));
    }
}
