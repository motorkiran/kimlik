using System.ComponentModel;
using Kimlik.Application.Permissions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

internal static class PermissionEndpoints
{
    public static IEndpointRouteBuilder MapPermissionEndpoints(this IEndpointRouteBuilder api)
    {
        var permissions = api.MapGroup("permissions").WithTags("Permissions");

        permissions.MapGet(string.Empty, ListAsync)
            .WithName("ListPermissions")
            .WithSummary("List permissions")
            .WithDescription("Lists the permissions of your applications and of Kimlik itself, in creation order.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.RolesRead);

        permissions.MapPost(string.Empty, CreateAsync)
            .WithName("CreatePermission")
            .WithSummary("Create a permission")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.RolesWrite);

        permissions.MapGet("{id:guid}", GetAsync)
            .WithName("GetPermission")
            .WithSummary("Get a permission")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.RolesRead);

        permissions.MapPatch("{id:guid}", UpdateAsync)
            .WithName("UpdatePermission")
            .WithSummary("Update a permission's description")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.RolesWrite);

        permissions.MapDelete("{id:guid}", DeleteAsync)
            .WithName("DeletePermission")
            .WithSummary("Delete a permission")
            .WithDescription("Also removes the permission from every role.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.RolesWrite);

        return api;
    }

    private static async Task<Results<Ok<Page<PermissionResponse>>, ProblemHttpResult>> ListAsync(
        [Description("Part of the key.")] string? q,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListPermissionsHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListPermissionsQuery(q, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<PermissionResponse>, ProblemHttpResult>> CreateAsync(
        CreatePermissionRequest request, CreatePermissionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(permission => $"{ManagementApi.BasePath}/permissions/{permission.Id}");

    private static async Task<Results<Ok<PermissionResponse>, ProblemHttpResult>> GetAsync(
        Guid id, GetPermissionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<PermissionResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdatePermissionRequest request, UpdatePermissionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, DeletePermissionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();
}
