using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Common;
using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Organizations;

/// <summary>The signed-in user's own organizations: listing, creating and leaving them.</summary>
public sealed class MyOrganizations(
    IKimlikDbContext context,
    OrganizationGuard guard,
    CreateOrganizationHandler createOrganization,
    RemoveMemberHandler removeMember,
    IAuditLog auditLog,
    IOptions<OrganizationOptions> options,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<MyOrganizationResponse>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await DescribeAsync(userId, organizationId: null, cancellationToken);

    /// <summary>The organization as the member sees it.</summary>
    internal async Task<MyOrganizationResponse> DescribeAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        (await DescribeAsync(userId, (Guid?)organizationId, cancellationToken)).Single();

    private async Task<List<MyOrganizationResponse>> DescribeAsync(Guid userId, Guid? organizationId, CancellationToken cancellationToken)
    {
        var memberships = await context.Memberships.AsNoTracking()
            .Where(membership => membership.UserId == userId && (organizationId == null || membership.OrganizationId == organizationId))
            .Join(context.Organizations, membership => membership.OrganizationId, organization => organization.Id, (membership, organization) => new { membership, organization })
            .OrderBy(entry => entry.organization.Name)
            .ToListAsync(cancellationToken);

        var members = await context.ToMemberResponsesAsync([.. memberships.Select(entry => entry.membership)], cancellationToken);

        var result = new List<MyOrganizationResponse>(memberships.Count);
        foreach (var (entry, member) in memberships.Zip(members))
        {
            var permissions = await guard.PermissionsOfMemberAsync(entry.organization.Id, userId, cancellationToken);
            result.Add(new MyOrganizationResponse(
                entry.organization.Id,
                entry.organization.Name,
                entry.organization.Slug,
                member.Roles,
                [.. permissions.Order(StringComparer.Ordinal)],
                member.JoinedAt,
                entry.organization.RequireMfa,
                Metadata.Parse(entry.organization.PublicMetadata)));
        }

        return result;
    }

    /// <summary>Creates an organization with the user as its first member, holding the creator roles.</summary>
    public async Task<Result<MyOrganizationResponse>> CreateAsync(Guid userId, CreateMyOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (!options.Value.UsersCanCreate)
        {
            return OrganizationErrors.CreationDisabled;
        }

        var creatorRoles = await RoleSet.ResolveOrganizationAsync(context, options.Value.CreatorRoles, cancellationToken);
        if (creatorRoles.IsFailure)
        {
            throw new InvalidOperationException($"{OrganizationOptions.SectionName}:CreatorRoles must name existing organization roles.");
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        var created = await createOrganization.HandleAsync(
            new CreateOrganizationRequest { Name = request.Name, Slug = request.Slug, RequireMfa = request.RequireMfa }, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var now = timeProvider.GetUtcNow();
        var membership = Membership.Create(created.Value.Id, userId, now);
        var assigned = membership.SetRoles(creatorRoles.Value, now);
        if (assigned.IsFailure)
        {
            return assigned.Error;
        }

        context.Memberships.Add(membership);
        auditLog.Record(
            AuditActions.MembershipCreated,
            AuditSubject.User(userId),
            AddMemberHandler.RolesData(creatorRoles.Value.Select(role => role.Key)),
            organizationId: created.Value.Id);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(userId, created.Value.Id, cancellationToken);
    }

    /// <summary>
    /// Leaves an organization. Someone else must be able to manage the members afterwards, unless nobody else is
    /// left.
    /// </summary>
    public async Task<Result> LeaveAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var otherMembers = await context.Memberships
            .Where(membership => membership.OrganizationId == organizationId && membership.UserId != userId)
            .Select(membership => membership.UserId)
            .ToListAsync(cancellationToken);

        if (otherMembers.Count > 0
            && (await guard.PermissionsOfMemberAsync(organizationId, userId, cancellationToken)).Contains(SystemPermissions.OrganizationMembersWrite))
        {
            var anotherManager = false;
            foreach (var member in otherMembers)
            {
                if ((await guard.PermissionsOfMemberAsync(organizationId, member, cancellationToken)).Contains(SystemPermissions.OrganizationMembersWrite))
                {
                    anotherManager = true;
                    break;
                }
            }

            if (!anotherManager)
            {
                return OrganizationErrors.LastAdministrator;
            }
        }

        var removed = await removeMember.HandleAsync(organizationId, userId, cancellationToken);
        return removed.IsFailure && removed.Error == OrganizationErrors.MemberNotFound ? OrganizationErrors.NotFound : removed;
    }
}

/// <summary>Invitations to the signed-in user's verified address.</summary>
public sealed class MyInvitations(IKimlikDbContext context, AcceptInvitationHandler acceptInvitation, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<MyInvitationResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var invitations = await PendingForAsync(userId, now)
            .Join(context.Organizations, invitation => invitation.OrganizationId, organization => organization.Id, (invitation, organization) => new { invitation, organization.Name })
            .OrderByDescending(entry => entry.invitation.Id)
            .ToListAsync(cancellationToken);

        var details = await context.ToResponsesAsync([.. invitations.Select(entry => entry.invitation)], now, cancellationToken);
        return [.. invitations.Zip(details, (entry, detail) =>
            new MyInvitationResponse(entry.invitation.Id, entry.invitation.OrganizationId, entry.Name, detail.Roles, entry.invitation.ExpiresAt))];
    }

    public async Task<Result<Guid>> AcceptAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (await FindAsync(userId, invitationId, now, cancellationToken) is not { } invitation)
        {
            return OrganizationErrors.InvitationNotFound;
        }

        return await acceptInvitation.AcceptAsync(invitation, userId, verifiesEmail: false, now, cancellationToken);
    }

    public async Task<Result> DeclineAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (await FindAsync(userId, invitationId, now, cancellationToken) is not { } invitation)
        {
            return OrganizationErrors.InvitationNotFound;
        }

        invitation.Revoke(now);
        auditLog.Record(AuditActions.InvitationDeclined, AuditSubject.Invitation(invitation.Id), organizationId: invitation.OrganizationId);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Task<Invitation?> FindAsync(Guid userId, Guid invitationId, DateTimeOffset now, CancellationToken cancellationToken) =>
        PendingForAsync(userId, now).Include(invitation => invitation.Roles).SingleOrDefaultAsync(invitation => invitation.Id == invitationId, cancellationToken);

    /// <summary>Open invitations to the user's address, if the user has verified it.</summary>
    private IQueryable<Invitation> PendingForAsync(Guid userId, DateTimeOffset now) =>
        context.Invitations.Where(invitation =>
            invitation.Status == Domain.Organizations.InvitationStatus.Pending
            && invitation.ExpiresAt > now
            && context.Users.Any(user => user.Id == userId && user.EmailConfirmed && user.NormalizedEmail == invitation.NormalizedEmail));
}
