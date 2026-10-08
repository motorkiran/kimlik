using System.ComponentModel;
using Kimlik.Application.Common;
using Kimlik.Application.Roles;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;
using RoleScope = Kimlik.Contracts.Management.RoleScope;

namespace Kimlik.Server.Api;

internal static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder api)
    {
        var roles = api.MapGroup("roles").WithTags("Roles");

        roles.MapGet(string.Empty, ListAsync)
            .WithName("ListRoles")
            .WithSummary("List roles")
            .WithDescription("Lists roles in creation order.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.RolesRead);

        roles.MapPost(string.Empty, CreateAsync)
            .WithName("CreateRole")
            .WithSummary("Create a role")
            .WithDescription("Including system permissions requires holding them.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.RolesWrite);

        roles.MapGet("{id:guid}", GetAsync)
            .WithName("GetRole")
            .WithSummary("Get a role")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.RolesRead);

        roles.MapPatch("{id:guid}", UpdateAsync)
            .WithName("UpdateRole")
            .WithSummary("Update a role's name or description")
            .WithDescription("JSON Merge Patch: omitted properties keep their value and `null` clears the description.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.RolesWrite);

        roles.MapPut("{id:guid}/permissions", SetPermissionsAsync)
            .WithName("SetRolePermissions")
            .WithSummary("Replace a role's permissions")
            .WithDescription("Tokens issued from then on carry the new permissions. Changing system permissions requires holding them.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.RolesWrite);

        roles.MapDelete("{id:guid}", DeleteAsync)
            .WithName("DeleteRole")
            .WithSummary("Delete a role")
            .WithDescription("Also removes the role from every user and client that holds it.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.RolesWrite);

        return api;
    }

    private static async Task<Results<Ok<Page<RoleResponse>>, ProblemHttpResult>> ListAsync(
        [Description("Part of the key or name.")] string? q,
        [Description("`global` or `organization`.")] string? scope,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListRolesHandler handler,
        CancellationToken cancellationToken)
    {
        if (!QueryValues.TryParseEnum<RoleScope>(scope, out var scopeFilter))
        {
            return ApiResults.Problem(CommonErrors.InvalidParameter(nameof(scope)));
        }

        return (await handler.HandleAsync(new ListRolesQuery(q, scopeFilter, cursor, limit), cancellationToken)).ToOk();
    }

    private static async Task<Results<Created<RoleResponse>, ProblemHttpResult>> CreateAsync(
        CreateRoleRequest request, CreateRoleHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(role => $"{ManagementApi.BasePath}/roles/{role.Id}");

    private static async Task<Results<Ok<RoleResponse>, ProblemHttpResult>> GetAsync(
        Guid id, GetRoleHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<RoleResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateRoleRequest request, UpdateRoleHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<Ok<RoleResponse>, ProblemHttpResult>> SetPermissionsAsync(
        Guid id, SetPermissionsRequest request, SetRolePermissionsHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, DeleteRoleHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();
}
