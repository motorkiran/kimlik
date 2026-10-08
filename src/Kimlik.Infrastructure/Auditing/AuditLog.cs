using System.Text.Json;
using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Infrastructure.Persistence;

namespace Kimlik.Infrastructure.Auditing;

internal sealed class AuditLog(KimlikDbContext context, IRequestContext request, TimeProvider timeProvider) : IAuditLog
{
    public void Record(
        string action,
        AuditSubject? subject = null,
        IReadOnlyDictionary<string, object?>? data = null,
        AuditActor? actor = null,
        Guid? organizationId = null)
    {
        actor ??= request.Actor;

        context.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = timeProvider.GetUtcNow(),
            Action = action,
            ActorType = actor.Type,
            ActorId = actor.Id,
            SubjectType = subject?.Type,
            SubjectId = subject?.Id,
            OrganizationId = organizationId,
            IpAddress = request.IpAddress,
            UserAgent = Truncate(request.UserAgent, AuditEvent.UserAgentMaxLength),
            CorrelationId = Truncate(request.CorrelationId, AuditEvent.ReferenceMaxLength),
            Data = data is null ? null : JsonSerializer.Serialize(data),
        });
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;
}
