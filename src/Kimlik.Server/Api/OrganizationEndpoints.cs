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

        organizations.MapGet("{id:guid}/invitations", ListInvitationsAsync)
            .WithName("ListOrganizationInvitations")
            .WithSummary("List the invitations of an organization")
            .WithDescription("Lists invitations newest first, whatever their status.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.OrganizationsRead);

        organizations.MapPost("{id:guid}/invitations", InviteAsync)
            .WithName("CreateOrganizationInvitation")
            .WithSummary("Invite someone to an organization")
            .WithDescription("Emails a link that lets the owner of the address join with the given roles.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        organizations.MapPost("{id:guid}/invitations/{invitationId:guid}/resend", ResendInvitationAsync)
            .WithName("ResendOrganizationInvitation")
            .WithSummary("Send an invitation again")
            .WithDescription("Sends a new link with a new expiry; the earlier link stops working.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        organizations.MapDelete("{id:guid}/invitations/{invitationId:guid}", RevokeInvitationAsync)
            .WithName("RevokeOrganizationInvitation")
            .WithSummary("Revoke an invitation")
            .ProducesProblem(StatusCodes.Status400BadRequest)
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

    private static async Task<Results<Ok<Page<InvitationResponse>>, ProblemHttpResult>> ListInvitationsAsync(
        Guid id,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListInvitationsHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListInvitationsQuery(id, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<InvitationResponse>, ProblemHttpResult>> InviteAsync(
        Guid id, CreateInvitationRequest request, CreateInvitationHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToCreated(invitation => $"{ManagementApi.BasePath}/organizations/{id}/invitations/{invitation.Id}");

    private static async Task<Results<Ok<InvitationResponse>, ProblemHttpResult>> ResendInvitationAsync(
        Guid id, Guid invitationId, ResendInvitationHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, invitationId, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeInvitationAsync(
        Guid id, Guid invitationId, RevokeInvitationHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, invitationId, cancellationToken)).ToNoContent();
}
