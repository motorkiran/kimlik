using System.ComponentModel;
using Kimlik.Application.ApiResources;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

internal static class ApiResourceEndpoints
{
    public static IEndpointRouteBuilder MapApiResourceEndpoints(this IEndpointRouteBuilder api)
    {
        var resources = api.MapGroup("api-resources").WithTags("API resources");

        resources.MapGet(string.Empty, ListAsync)
            .WithName("ListApiResources")
            .WithSummary("List API resources")
            .WithDescription("Lists the APIs that accept Kimlik tokens, Kimlik's own included, in creation order.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.ClientsRead);

        resources.MapPost(string.Empty, CreateAsync)
            .WithName("CreateApiResource")
            .WithSummary("Register an API resource")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.ClientsWrite);

        resources.MapGet("{id:guid}", GetAsync)
            .WithName("GetApiResource")
            .WithSummary("Get an API resource")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.ClientsRead);

        resources.MapPatch("{id:guid}", UpdateAsync)
            .WithName("UpdateApiResource")
            .WithSummary("Update an API resource's display name or description")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.ClientsWrite);

        resources.MapDelete("{id:guid}", DeleteAsync)
            .WithName("DeleteApiResource")
            .WithSummary("Delete an API resource")
            .WithDescription("Also takes its scope away from every client. Access tokens already issued stay valid until they expire.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.ClientsWrite);

        return api;
    }

    private static async Task<Results<Ok<Page<ApiResourceResponse>>, ProblemHttpResult>> ListAsync(
        [Description("Part of the scope.")] string? q,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListApiResourcesHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListApiResourcesQuery(q, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<ApiResourceResponse>, ProblemHttpResult>> CreateAsync(
        CreateApiResourceRequest request, CreateApiResourceHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(resource => $"{ManagementApi.BasePath}/api-resources/{resource.Id}");

    private static async Task<Results<Ok<ApiResourceResponse>, ProblemHttpResult>> GetAsync(
        Guid id, GetApiResourceHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<ApiResourceResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateApiResourceRequest request, UpdateApiResourceHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, DeleteApiResourceHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();
}
