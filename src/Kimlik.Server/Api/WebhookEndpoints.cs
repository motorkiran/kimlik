using System.ComponentModel;
using Kimlik.Application.Common;
using Kimlik.Application.Webhooks;
using Kimlik.Contracts.Management;
using Kimlik.Contracts.Webhooks;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

/// <summary>Webhook endpoints, which receive signed events, and the log of their deliveries.</summary>
internal static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder api)
    {
        var webhooks = api.MapGroup("webhooks").WithTags("Webhooks");

        webhooks.MapGet("event-types", () => TypedResults.Ok<IReadOnlyList<string>>([.. WebhookEventTypes.All.Order(StringComparer.Ordinal)]))
            .WithName("ListWebhookEventTypes").WithSummary("List the events endpoints can subscribe to")
            .RequirePermission(SystemPermissions.WebhooksRead);

        webhooks.MapGet("endpoints", ListEndpointsAsync).WithName("ListWebhookEndpoints").WithSummary("List webhook endpoints")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.WebhooksRead);

        webhooks.MapPost("endpoints", CreateEndpointAsync).WithName("CreateWebhookEndpoint").WithSummary("Register a webhook endpoint")
            .WithDescription("Returns the secret that signs its deliveries, only this once. Deliveries follow the Standard Webhooks specification.")
            .ProducesValidationProblem()
            .RequirePermission(SystemPermissions.WebhooksWrite);

        webhooks.MapGet("endpoints/{id:guid}", GetEndpointAsync).WithName("GetWebhookEndpoint").WithSummary("Get a webhook endpoint")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.WebhooksRead);

        webhooks.MapPatch("endpoints/{id:guid}", UpdateEndpointAsync).WithName("UpdateWebhookEndpoint").WithSummary("Change a webhook endpoint")
            .WithDescription("Changes the URL, description, event types or whether it is enabled. Deliveries to a disabled endpoint fail instead of being sent.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.WebhooksWrite);

        webhooks.MapDelete("endpoints/{id:guid}", DeleteEndpointAsync).WithName("DeleteWebhookEndpoint").WithSummary("Delete a webhook endpoint")
            .WithDescription("Its delivery log is deleted with it.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.WebhooksWrite);

        webhooks.MapPost("endpoints/{id:guid}/secret", RotateSecretAsync).WithName("RotateWebhookSecret").WithSummary("Replace an endpoint's signing secret")
            .WithDescription("Returns the new secret, only this once. Every delivery from then on, retries included, is signed with it.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.WebhooksWrite);

        webhooks.MapPost("endpoints/{id:guid}/test", SendTestAsync).WithName("SendTestWebhook").WithSummary("Send a test event to an endpoint")
            .WithDescription("Sends a `webhook.test` event, even to a disabled endpoint, and returns its delivery.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.WebhooksWrite);

        webhooks.MapGet("deliveries", ListDeliveriesAsync).WithName("ListWebhookDeliveries").WithSummary("List webhook deliveries")
            .WithDescription("Lists deliveries newest first, with the outcome of their latest attempt. The log is kept for 30 days by default.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.WebhooksRead);

        webhooks.MapGet("deliveries/{id:guid}", GetDeliveryAsync).WithName("GetWebhookDelivery").WithSummary("Get a webhook delivery")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.WebhooksRead);

        webhooks.MapPost("deliveries/{id:guid}/redeliver", RedeliverAsync).WithName("RedeliverWebhook").WithSummary("Send a delivery again")
            .WithDescription("Sends the same event, with the same `webhook-id`, as a fresh series of attempts.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.WebhooksWrite);

        return api;
    }

    private static async Task<Results<Ok<Page<WebhookEndpointResponse>>, ProblemHttpResult>> ListEndpointsAsync(
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListWebhookEndpointsHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListWebhookEndpointsQuery(cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<CreatedWebhookEndpointResponse>, ProblemHttpResult>> CreateEndpointAsync(
        CreateWebhookEndpointRequest request, CreateWebhookEndpointHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(created => $"/api/v1/webhooks/endpoints/{created.Endpoint.Id}");

    private static async Task<Results<Ok<WebhookEndpointResponse>, ProblemHttpResult>> GetEndpointAsync(
        Guid id, GetWebhookEndpointHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<WebhookEndpointResponse>, ProblemHttpResult>> UpdateEndpointAsync(
        Guid id, UpdateWebhookEndpointRequest request, UpdateWebhookEndpointHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteEndpointAsync(
        Guid id, DeleteWebhookEndpointHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<WebhookSecretResponse>, ProblemHttpResult>> RotateSecretAsync(
        Guid id, RotateWebhookSecretHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<WebhookDeliveryResponse>, ProblemHttpResult>> SendTestAsync(
        Guid id, SendTestWebhookHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<Page<WebhookDeliveryResponse>>, ProblemHttpResult>> ListDeliveriesAsync(
        [Description("Only the deliveries to this endpoint.")] Guid? endpointId,
        [Description("`pending`, `succeeded` or `failed`.")] string? status,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListWebhookDeliveriesHandler handler,
        CancellationToken cancellationToken)
    {
        if (!QueryValues.TryParseEnum<WebhookDeliveryStatus>(status, out var parsedStatus))
        {
            return ApiResults.Problem(CommonErrors.InvalidParameter(nameof(status)));
        }

        return (await handler.HandleAsync(new ListWebhookDeliveriesQuery(endpointId, parsedStatus, cursor, limit), cancellationToken)).ToOk();
    }

    private static async Task<Results<Ok<WebhookDeliveryResponse>, ProblemHttpResult>> GetDeliveryAsync(
        Guid id, GetWebhookDeliveryHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<WebhookDeliveryResponse>, ProblemHttpResult>> RedeliverAsync(
        Guid id, RedeliverWebhookHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();
}
