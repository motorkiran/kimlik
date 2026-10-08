using System.ComponentModel;
using Kimlik.Application.Organizations;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

internal static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder api)
    {
        var organizations = api.MapGroup("organizations").WithTags("Organizations");

        organizations.MapGet(string.Empty, ListAsync)
            .WithName("ListOrganizations")
            .WithSummary("List organizations")
            .WithDescription("Lists organizations in creation order.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.OrganizationsRead);

        organizations.MapPost(string.Empty, CreateAsync)
            .WithName("CreateOrganization")
            .WithSummary("Create an organization")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        organizations.MapGet("{id:guid}", GetAsync)
            .WithName("GetOrganization")
            .WithSummary("Get an organization")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.OrganizationsRead);

        organizations.MapPatch("{id:guid}", UpdateAsync)
            .WithName("UpdateOrganization")
            .WithSummary("Rename an organization or change its slug")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        organizations.MapDelete("{id:guid}", DeleteAsync)
            .WithName("DeleteOrganization")
            .WithSummary("Delete an organization")
            .WithDescription("Also removes its memberships.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        organizations.MapGet("{id:guid}/members", ListMembersAsync)
            .WithName("ListOrganizationMembers")
            .WithSummary("List the members of an organization")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.OrganizationsRead);

        organizations.MapPost("{id:guid}/members", AddMemberAsync)
            .WithName("AddOrganizationMember")
            .WithSummary("Add a user to an organization")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        organizations.MapPut("{id:guid}/members/{userId:guid}/roles", SetMemberRolesAsync)
            .WithName("SetOrganizationMemberRoles")
            .WithSummary("Replace a member's organization roles")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        organizations.MapDelete("{id:guid}/members/{userId:guid}", RemoveMemberAsync)
            .WithName("RemoveOrganizationMember")
            .WithSummary("Remove a member from an organization")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        return api;
    }

    private static async Task<Results<Ok<Page<OrganizationResponse>>, ProblemHttpResult>> ListAsync(
        [Description("Part of the name or slug.")] string? q,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListOrganizationsHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListOrganizationsQuery(q, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<OrganizationResponse>, ProblemHttpResult>> CreateAsync(
        CreateOrganizationRequest request, CreateOrganizationHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(organization => $"{ManagementApi.BasePath}/organizations/{organization.Id}");

    private static async Task<Results<Ok<OrganizationResponse>, ProblemHttpResult>> GetAsync(
        Guid id, GetOrganizationHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<OrganizationResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateOrganizationRequest request, UpdateOrganizationHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, DeleteOrganizationHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<Page<MemberResponse>>, ProblemHttpResult>> ListMembersAsync(
        Guid id,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListMembersHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListMembersQuery(id, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<MemberResponse>, ProblemHttpResult>> AddMemberAsync(
        Guid id, AddMemberRequest request, AddMemberHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToCreated(member => $"{ManagementApi.BasePath}/organizations/{id}/members/{member.UserId}");

    private static async Task<Results<Ok<MemberResponse>, ProblemHttpResult>> SetMemberRolesAsync(
        Guid id, Guid userId, SetRolesRequest request, SetMemberRolesHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, userId, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveMemberAsync(
        Guid id, Guid userId, RemoveMemberHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, userId, cancellationToken)).ToNoContent();
}
