using System.ComponentModel;
using System.Security.Claims;
using Kimlik.Application.Organizations;
using Kimlik.Application.Users;
using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

/// <summary>The Account API: the signed-in user's own account, organizations and invitations.</summary>
internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("me").WithTags("Account").RequireSignedInUser();

        me.MapGet(string.Empty, GetAccountAsync).WithName("GetMyAccount").WithSummary("Get the signed-in user");

        me.MapGet("organizations", ListOrganizationsAsync).WithName("ListMyOrganizations")
            .WithSummary("List my organizations")
            .WithDescription("The organizations the user belongs to, with the user's roles and permissions in each.");

        me.MapPost("organizations", CreateOrganizationAsync).WithName("CreateMyOrganization")
            .WithSummary("Create an organization")
            .WithDescription("The user becomes its first member, with the creator roles of the installation.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPatch("organizations/{id:guid}", UpdateOrganizationAsync).WithName("UpdateMyOrganization")
            .WithSummary("Rename an organization or change its slug")
            .WithDescription("Requires `kimlik.org.settings:write` in the organization.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("organizations/{id:guid}", DeleteOrganizationAsync).WithName("DeleteMyOrganization")
            .WithSummary("Delete an organization")
            .WithDescription("Requires `kimlik.org.settings:write` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("organizations/{id:guid}/membership", LeaveOrganizationAsync).WithName("LeaveOrganization")
            .WithSummary("Leave an organization")
            .WithDescription("Someone else must be able to manage the members afterwards, unless nobody else is left.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapGet("organizations/{id:guid}/members", ListMembersAsync).WithName("ListMyOrganizationMembers")
            .WithSummary("List the members of an organization")
            .WithDescription("Requires `kimlik.org.members:read` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPut("organizations/{id:guid}/members/{userId:guid}/roles", SetMemberRolesAsync).WithName("SetMyOrganizationMemberRoles")
            .WithSummary("Replace a member's roles")
            .WithDescription("Requires `kimlik.org.members:write` in the organization, and every organization permission involved.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("organizations/{id:guid}/members/{userId:guid}", RemoveMemberAsync).WithName("RemoveMyOrganizationMember")
            .WithSummary("Remove a member")
            .WithDescription("Requires `kimlik.org.members:write` in the organization, and every organization permission the member holds.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("organizations/{id:guid}/invitations", ListInvitationsAsync).WithName("ListMyOrganizationInvitations")
            .WithSummary("List the invitations of an organization")
            .WithDescription("Requires `kimlik.org.members:read` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("organizations/{id:guid}/invitations", InviteAsync).WithName("CreateMyOrganizationInvitation")
            .WithSummary("Invite someone to an organization")
            .WithDescription("Requires `kimlik.org.members:write` in the organization, and every organization permission the roles carry.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPost("organizations/{id:guid}/invitations/{invitationId:guid}/resend", ResendInvitationAsync).WithName("ResendMyOrganizationInvitation")
            .WithSummary("Send an invitation again")
            .WithDescription("Requires `kimlik.org.members:write` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("organizations/{id:guid}/invitations/{invitationId:guid}", RevokeInvitationAsync).WithName("RevokeMyOrganizationInvitation")
            .WithSummary("Revoke an invitation")
            .WithDescription("Requires `kimlik.org.members:write` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("invitations", ListInvitationsToMeAsync).WithName("ListMyInvitations")
            .WithSummary("List invitations to me")
            .WithDescription("Pending invitations to the user's verified email address.");

        me.MapPost("invitations/{id:guid}/accept", AcceptInvitationAsync).WithName("AcceptMyInvitation")
            .WithSummary("Accept an invitation")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("invitations/{id:guid}/decline", DeclineInvitationAsync).WithName("DeclineMyInvitation")
            .WithSummary("Decline an invitation")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }

    private static Guid Caller(ClaimsPrincipal principal) => AccountCaller.UserId(principal)!.Value;

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> GetAccountAsync(
        ClaimsPrincipal principal, GetUserHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(Caller(principal), cancellationToken)).ToOk();

    private static async Task<Ok<IReadOnlyList<MyOrganizationResponse>>> ListOrganizationsAsync(
        ClaimsPrincipal principal, MyOrganizations organizations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await organizations.ListAsync(Caller(principal), cancellationToken));

    private static async Task<Results<Created<OrganizationResponse>, ProblemHttpResult>> CreateOrganizationAsync(
        ClaimsPrincipal principal, CreateOrganizationRequest request, MyOrganizations organizations, CancellationToken cancellationToken) =>
        (await organizations.CreateAsync(Caller(principal), request, cancellationToken)).ToCreated(organization => $"{ManagementApi.BasePath}/me/organizations/{organization.Id}");

    private static async Task<Results<Ok<OrganizationResponse>, ProblemHttpResult>> UpdateOrganizationAsync(
        ClaimsPrincipal principal, Guid id, UpdateOrganizationRequest request, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(Caller(principal), id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteOrganizationAsync(
        ClaimsPrincipal principal, Guid id, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(Caller(principal), id, cancellationToken)).ToNoContent();

    private static async Task<Results<NoContent, ProblemHttpResult>> LeaveOrganizationAsync(
        ClaimsPrincipal principal, Guid id, MyOrganizations organizations, CancellationToken cancellationToken) =>
        (await organizations.LeaveAsync(Caller(principal), id, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<Page<MemberResponse>>, ProblemHttpResult>> ListMembersAsync(
        ClaimsPrincipal principal,
        Guid id,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        OrganizationSelfService service,
        CancellationToken cancellationToken) =>
        (await service.ListMembersAsync(Caller(principal), new ListMembersQuery(id, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Ok<MemberResponse>, ProblemHttpResult>> SetMemberRolesAsync(
        ClaimsPrincipal principal, Guid id, Guid userId, SetRolesRequest request, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.SetMemberRolesAsync(Caller(principal), id, userId, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveMemberAsync(
        ClaimsPrincipal principal, Guid id, Guid userId, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.RemoveMemberAsync(Caller(principal), id, userId, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<Page<InvitationResponse>>, ProblemHttpResult>> ListInvitationsAsync(
        ClaimsPrincipal principal,
        Guid id,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        OrganizationSelfService service,
        CancellationToken cancellationToken) =>
        (await service.ListInvitationsAsync(Caller(principal), new ListInvitationsQuery(id, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<InvitationResponse>, ProblemHttpResult>> InviteAsync(
        ClaimsPrincipal principal, Guid id, CreateInvitationRequest request, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.InviteAsync(Caller(principal), id, request, cancellationToken))
            .ToCreated(invitation => $"{ManagementApi.BasePath}/me/organizations/{id}/invitations/{invitation.Id}");

    private static async Task<Results<Ok<InvitationResponse>, ProblemHttpResult>> ResendInvitationAsync(
        ClaimsPrincipal principal, Guid id, Guid invitationId, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.ResendInvitationAsync(Caller(principal), id, invitationId, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeInvitationAsync(
        ClaimsPrincipal principal, Guid id, Guid invitationId, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.RevokeInvitationAsync(Caller(principal), id, invitationId, cancellationToken)).ToNoContent();

    private static async Task<Ok<IReadOnlyList<MyInvitationResponse>>> ListInvitationsToMeAsync(
        ClaimsPrincipal principal, MyInvitations invitations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await invitations.ListAsync(Caller(principal), cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> AcceptInvitationAsync(
        ClaimsPrincipal principal, Guid id, MyInvitations invitations, CancellationToken cancellationToken)
    {
        var accepted = await invitations.AcceptAsync(Caller(principal), id, cancellationToken);
        return accepted.IsSuccess ? TypedResults.NoContent() : ApiResults.Problem(accepted.Error);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeclineInvitationAsync(
        ClaimsPrincipal principal, Guid id, MyInvitations invitations, CancellationToken cancellationToken) =>
        (await invitations.DeclineAsync(Caller(principal), id, cancellationToken)).ToNoContent();
}
