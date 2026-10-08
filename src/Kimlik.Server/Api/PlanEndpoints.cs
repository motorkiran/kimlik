using System.ComponentModel;
using Kimlik.Application.Plans;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

internal static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder api)
    {
        var features = api.MapGroup("features").WithTags("Features");

        features.MapGet(string.Empty, ListFeaturesAsync).WithName("ListFeatures").WithSummary("List features")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.PlansRead);

        features.MapPost(string.Empty, CreateFeatureAsync).WithName("CreateFeature").WithSummary("Define a feature")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.PlansWrite);

        features.MapGet("{id:guid}", GetFeatureAsync).WithName("GetFeature").WithSummary("Get a feature")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.PlansRead);

        features.MapPatch("{id:guid}", UpdateFeatureAsync).WithName("UpdateFeature").WithSummary("Rename or describe a feature")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.PlansWrite);

        features.MapDelete("{id:guid}", DeleteFeatureAsync).WithName("DeleteFeature").WithSummary("Delete a feature")
            .WithDescription("Also removes its value from every plan.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.PlansWrite);

        var plans = api.MapGroup("plans").WithTags("Plans");

        plans.MapGet(string.Empty, ListPlansAsync).WithName("ListPlans").WithSummary("List plans")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.PlansRead);

        plans.MapPost(string.Empty, CreatePlanAsync).WithName("CreatePlan").WithSummary("Define a plan")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.PlansWrite);

        plans.MapGet("{id:guid}", GetPlanAsync).WithName("GetPlan").WithSummary("Get a plan")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.PlansRead);

        plans.MapPatch("{id:guid}", UpdatePlanAsync).WithName("UpdatePlan").WithSummary("Change a plan")
            .WithDescription("New feature values apply to the plan's subscribers at once.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.PlansWrite);

        plans.MapDelete("{id:guid}", DeletePlanAsync).WithName("DeletePlan").WithSummary("Delete a plan")
            .WithDescription("Only a plan without subscriptions can be deleted; archive the others.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.PlansWrite);

        return api;
    }

    private static async Task<Results<Ok<Page<FeatureResponse>>, ProblemHttpResult>> ListFeaturesAsync(
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListFeaturesHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(cursor, limit, cancellationToken)).ToOk();

    private static async Task<Results<Created<FeatureResponse>, ProblemHttpResult>> CreateFeatureAsync(
        CreateFeatureRequest request, CreateFeatureHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(feature => $"{ManagementApi.BasePath}/features/{feature.Id}");

    private static async Task<Results<Ok<FeatureResponse>, ProblemHttpResult>> GetFeatureAsync(
        Guid id, GetFeatureHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<FeatureResponse>, ProblemHttpResult>> UpdateFeatureAsync(
        Guid id, UpdateFeatureRequest request, UpdateFeatureHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteFeatureAsync(
        Guid id, DeleteFeatureHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<Page<PlanResponse>>, ProblemHttpResult>> ListPlansAsync(
        [Description("Only archived plans, or only plans open to new subscribers.")] bool? archived,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListPlansHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListPlansQuery(archived, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<PlanResponse>, ProblemHttpResult>> CreatePlanAsync(
        CreatePlanRequest request, CreatePlanHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(plan => $"{ManagementApi.BasePath}/plans/{plan.Id}");

    private static async Task<Results<Ok<PlanResponse>, ProblemHttpResult>> GetPlanAsync(
        Guid id, GetPlanHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<PlanResponse>, ProblemHttpResult>> UpdatePlanAsync(
        Guid id, UpdatePlanRequest request, UpdatePlanHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeletePlanAsync(
        Guid id, DeletePlanHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();
}
