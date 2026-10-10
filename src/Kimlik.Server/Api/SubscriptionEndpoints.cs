using System.ComponentModel;
using Kimlik.Application.Common;
using Kimlik.Application.Plans;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

internal static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder api)
    {
        var subscriptions = api.MapGroup("subscriptions").WithTags("Subscriptions");

        subscriptions.MapGet(string.Empty, ListAsync).WithName("ListSubscriptions").WithSummary("List subscriptions")
            .WithDescription("Lists subscriptions newest first, ended ones included; filter by subscriber to see one's history.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.SubscriptionsRead);

        subscriptions.MapPost(string.Empty, CreateAsync).WithName("CreateSubscription").WithSummary("Subscribe a user or an organization to a plan")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.SubscriptionsWrite);

        subscriptions.MapGet("{id:guid}", GetAsync).WithName("GetSubscription").WithSummary("Get a subscription")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.SubscriptionsRead);

        subscriptions.MapPatch("{id:guid}", UpdateAsync).WithName("UpdateSubscription")
            .WithSummary("Change a subscription's plan, period or status")
            .WithDescription("Tokens issued from then on carry the new plan.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.SubscriptionsWrite);

        subscriptions.MapPost("{id:guid}/cancel", CancelAsync).WithName("CancelSubscription").WithSummary("Cancel a subscription")
            .WithDescription("It stays in effect until the end of its period; one without an end expires at once.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.SubscriptionsWrite);

        api.MapGet("entitlements/{subscriberType}/{id:guid}", GetEntitlementsAsync).WithTags("Subscriptions").WithName("GetEntitlements")
            .WithSummary("Get what a user or an organization is entitled to")
            .WithDescription("The plan in effect and the value of every feature. `subscriberType` is `user` or `organization`.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.SubscriptionsRead);

        var usage = api.MapGroup("usage").WithTags("Usage");
        usage.MapPost(string.Empty, RecordUsageAsync).WithName("RecordUsage")
            .WithSummary("Record use of a metered limit")
            .WithDescription("Use counts by calendar month, in UTC. With `enforce`, use that would go past the month's limit is refused "
                + "(`usage.limit_reached`) and not recorded; an `idempotencyKey` makes a retry within a day count once.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.UsageWrite);

        usage.MapGet("{subscriberType}/{id:guid}", GetUsageAsync).WithName("GetUsage")
            .WithSummary("Get a user's or an organization's use of metered limits this month")
            .WithDescription("`subscriberType` is `user` or `organization`.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.UsageRead);

        return api;
    }

    private static async Task<Results<Ok<Page<SubscriptionResponse>>, ProblemHttpResult>> ListAsync(
        [Description("`user` or `organization`; set it together with `subscriberId`.")] string? subscriberType,
        [Description("The ID of the user or organization.")] Guid? subscriberId,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListSubscriptionsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!QueryValues.TryParseEnum<SubscriberType>(subscriberType, out var type))
        {
            return ApiResults.Problem(CommonErrors.InvalidParameter(nameof(subscriberType)));
        }

        return (await handler.HandleAsync(new ListSubscriptionsQuery(type, subscriberId, cursor, limit), cancellationToken)).ToOk();
    }

    private static async Task<Results<Created<SubscriptionResponse>, ProblemHttpResult>> CreateAsync(
        CreateSubscriptionRequest request, CreateSubscriptionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(subscription => $"{ManagementApi.BasePath}/subscriptions/{subscription.Id}");

    private static async Task<Results<Ok<SubscriptionResponse>, ProblemHttpResult>> GetAsync(
        Guid id, GetSubscriptionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<SubscriptionResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateSubscriptionRequest request, UpdateSubscriptionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<Ok<SubscriptionResponse>, ProblemHttpResult>> CancelAsync(
        Guid id, CancelSubscriptionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<UsageResponse>, ProblemHttpResult>> RecordUsageAsync(
        RecordUsageRequest request, RecordUsageHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToOk();

    private static async Task<Results<Ok<IReadOnlyList<UsageResponse>>, ProblemHttpResult>> GetUsageAsync(
        string subscriberType, Guid id, GetUsageHandler handler, CancellationToken cancellationToken) =>
        QueryValues.TryParseEnum<SubscriberType>(subscriberType, out var type) && type is { } subscriber
            ? (await handler.HandleAsync(subscriber, id, cancellationToken)).ToOk()
            : ApiResults.Problem(CommonErrors.InvalidParameter(nameof(subscriberType)));

    private static async Task<Results<Ok<EntitlementsResponse>, ProblemHttpResult>> GetEntitlementsAsync(
        string subscriberType, Guid id, Entitlements entitlements, CancellationToken cancellationToken)
    {
        if (!QueryValues.TryParseEnum<SubscriberType>(subscriberType, out var type) || type is null)
        {
            return ApiResults.Problem(CommonErrors.InvalidParameter(nameof(subscriberType)));
        }

        var subscriber = type == SubscriberType.User ? Domain.Plans.Subscriber.User(id) : Domain.Plans.Subscriber.Organization(id);
        return TypedResults.Ok(await entitlements.DescribeAsync(subscriber, cancellationToken));
    }
}
