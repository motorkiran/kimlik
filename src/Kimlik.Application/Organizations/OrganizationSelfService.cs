using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;

namespace Kimlik.Application.Organizations;

/// <summary>
/// Organization management by its own members, as their organization roles allow: the same operations as the
/// Management API, after <see cref="OrganizationGuard"/> checks the caller's <c>kimlik.org.*</c> permissions.
/// </summary>
public sealed class OrganizationSelfService(
    IKimlikDbContext context,
    OrganizationGuard guard,
    MyOrganizations myOrganizations,
    UpdateOrganizationHandler updateOrganization,
    DeleteOrganizationHandler deleteOrganization,
    ListMembersHandler listMembers,
    SetMemberRolesHandler setMemberRoles,
    RemoveMemberHandler removeMember,
    ListInvitationsHandler listInvitations,
    CreateInvitationHandler createInvitation,
    ResendInvitationHandler resendInvitation,
    RevokeInvitationHandler revokeInvitation)
{
    public async Task<Result<MyOrganizationResponse>> UpdateAsync(Guid callerId, Guid organizationId, UpdateMyOrganizationRequest request, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, organizationId, SystemPermissions.OrganizationSettingsWrite, cancellationToken);
        if (caller.IsFailure)
        {
            return caller.Error;
        }

        // Only what the member sends is set, so that omitted properties keep their value.
        var update = new UpdateOrganizationRequest { RequireMfa = request.RequireMfa };
        update = request.HasName ? update with { Name = request.Name } : update;
        update = request.HasSlug ? update with { Slug = request.Slug } : update;

        var updated = await updateOrganization.HandleAsync(organizationId, update, cancellationToken);
        return updated.IsFailure ? updated.Error : await myOrganizations.DescribeAsync(callerId, organizationId, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid callerId, Guid organizationId, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, organizationId, SystemPermissions.OrganizationSettingsWrite, cancellationToken);
        return caller.IsFailure ? caller.Error : await deleteOrganization.HandleAsync(organizationId, cancellationToken);
    }

    public async Task<Result<Page<MemberResponse>>> ListMembersAsync(Guid callerId, ListMembersQuery query, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, query.OrganizationId, SystemPermissions.OrganizationMembersRead, cancellationToken);
        return caller.IsFailure ? caller.Error : await listMembers.HandleAsync(query, cancellationToken);
    }

    public async Task<Result<MemberResponse>> SetMemberRolesAsync(
        Guid callerId, Guid organizationId, Guid memberId, SetRolesRequest request, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, organizationId, SystemPermissions.OrganizationMembersWrite, cancellationToken);
        if (caller.IsFailure)
        {
            return caller.Error;
        }

        var check = await EnsureCanAssignAsync(caller.Value, organizationId, memberId, request.Roles, cancellationToken);
        return check.IsFailure ? check.Error : await setMemberRoles.HandleAsync(organizationId, memberId, request, cancellationToken);
    }

    /// <summary>Removes a member; removing oneself is leaving, which keeps someone able to manage the members.</summary>
    public async Task<Result> RemoveMemberAsync(Guid callerId, Guid organizationId, Guid memberId, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, organizationId, SystemPermissions.OrganizationMembersWrite, cancellationToken);
        if (caller.IsFailure)
        {
            return caller.Error;
        }

        if (memberId == callerId)
        {
            return await myOrganizations.LeaveAsync(callerId, organizationId, cancellationToken);
        }

        var check = await guard.EnsureCanManageAsync(caller.Value, organizationId, memberId, cancellationToken);
        return check.IsFailure ? check : await removeMember.HandleAsync(organizationId, memberId, cancellationToken);
    }

    public async Task<Result<Page<InvitationResponse>>> ListInvitationsAsync(Guid callerId, ListInvitationsQuery query, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, query.OrganizationId, SystemPermissions.OrganizationMembersRead, cancellationToken);
        return caller.IsFailure ? caller.Error : await listInvitations.HandleAsync(query, cancellationToken);
    }

    public async Task<Result<InvitationResponse>> InviteAsync(Guid callerId, Guid organizationId, CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, organizationId, SystemPermissions.OrganizationMembersWrite, cancellationToken);
        if (caller.IsFailure)
        {
            return caller.Error;
        }

        var roles = await RoleSet.ResolveOrganizationAsync(context, request.Roles, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        var check = await guard.EnsureCanGrantAsync(caller.Value, [.. roles.Value.Select(role => role.Id)], cancellationToken);
        return check.IsFailure ? check.Error : await createInvitation.HandleAsync(organizationId, request, cancellationToken);
    }

    public async Task<Result<InvitationResponse>> ResendInvitationAsync(Guid callerId, Guid organizationId, Guid invitationId, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, organizationId, SystemPermissions.OrganizationMembersWrite, cancellationToken);
        return caller.IsFailure ? caller.Error : await resendInvitation.HandleAsync(organizationId, invitationId, cancellationToken);
    }

    public async Task<Result> RevokeInvitationAsync(Guid callerId, Guid organizationId, Guid invitationId, CancellationToken cancellationToken)
    {
        var caller = await guard.AuthorizeAsync(callerId, organizationId, SystemPermissions.OrganizationMembersWrite, cancellationToken);
        return caller.IsFailure ? caller.Error : await revokeInvitation.HandleAsync(organizationId, invitationId, cancellationToken);
    }

    private async Task<Result> EnsureCanAssignAsync(
        HashSet<string> callerPermissions, Guid organizationId, Guid memberId, IReadOnlyCollection<string> roleKeys, CancellationToken cancellationToken)
    {
        var manage = await guard.EnsureCanManageAsync(callerPermissions, organizationId, memberId, cancellationToken);
        if (manage.IsFailure)
        {
            return manage;
        }

        var roles = await RoleSet.ResolveOrganizationAsync(context, roleKeys, cancellationToken);
        return roles.IsFailure ? roles.Error : await guard.EnsureCanGrantAsync(callerPermissions, [.. roles.Value.Select(role => role.Id)], cancellationToken);
    }
}
