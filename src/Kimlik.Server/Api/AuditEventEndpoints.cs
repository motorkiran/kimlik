using System.ComponentModel;
using Kimlik.Application.Auditing;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;
using AuditActorType = Kimlik.Contracts.Management.AuditActorType;

namespace Kimlik.Server.Api;

internal static class AuditEventEndpoints
{
    public static IEndpointRouteBuilder MapAuditEventEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("audit-events", ListAsync)
            .WithTags("Audit events")
            .WithName("ListAuditEvents")
            .WithSummary("Query the audit trail")
            .WithDescription("Lists audit events, newest first. Every filter is optional, and filters combine.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.AuditRead);

        return api;
    }

    private static async Task<Results<Ok<Page<AuditEventResponse>>, ProblemHttpResult>> ListAsync(
        [Description("An action such as `user.suspended`.")] string? action,
        [Description("`anonymous`, `user`, `client` or `system`.")] string? actorType,
        [Description("A user ID or client ID.")] string? actorId,
        [Description("What was acted on, such as `user` or `role`.")] string? subjectType,
        [Description("The ID of what was acted on.")] string? subjectId,
        [Description("Events within this organization.")] Guid? organizationId,
        [Description("Events at or after this time (ISO 8601).")] DateTimeOffset? from,
        [Description("Events before this time (ISO 8601).")] DateTimeOffset? to,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListAuditEventsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!QueryValues.TryParseEnum<AuditActorType>(actorType, out var actorTypeFilter))
        {
            return ApiResults.Problem(CommonErrors.InvalidParameter(nameof(actorType)));
        }

        var query = new ListAuditEventsQuery(action, actorTypeFilter, actorId, subjectType, subjectId, organizationId, from, to, cursor, limit);
        return (await handler.HandleAsync(query, cancellationToken)).ToOk();
    }
}
