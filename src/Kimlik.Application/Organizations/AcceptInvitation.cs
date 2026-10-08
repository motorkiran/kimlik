using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Organizations;

/// <summary>What the invitation page shows before the invitation is accepted.</summary>
public sealed record InvitationPreview(Guid Id, string OrganizationName, string Email);

/// <summary>Reads the invitation behind a link, if it can still be accepted.</summary>
public sealed class FindInvitationHandler(IKimlikDbContext context, TimeProvider timeProvider)
{
    public async Task<Result<InvitationPreview>> HandleAsync(string token, CancellationToken cancellationToken)
    {
        if (await FindOpenAsync(context, token, timeProvider.GetUtcNow(), cancellationToken) is not { } invitation)
        {
            return OrganizationErrors.InvitationClosed;
        }

        var organization = await context.Organizations.AsNoTracking().SingleAsync(organization => organization.Id == invitation.OrganizationId, cancellationToken);
        return new InvitationPreview(invitation.Id, organization.Name, invitation.Email);
    }

    /// <summary>An unknown token gets the same answer as a closed invitation, so links cannot be probed.</summary>
    internal static async Task<Invitation?> FindOpenAsync(IKimlikDbContext context, string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hash = InvitationTokens.Hash(token);
        var invitation = await context.Invitations.Include(invitation => invitation.Roles).SingleOrDefaultAsync(invitation => invitation.TokenHash == hash, cancellationToken);
        return invitation is not null && invitation.IsOpenAt(now) ? invitation : null;
    }
}

/// <summary>
/// Accepts an invitation for the signed-in user, who must hold the invited address. The link proves access to
/// that inbox, so it also verifies the address. A user who is already a member gets the invitation's roles too.
/// </summary>
public sealed class AcceptInvitationHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<Guid>> HandleAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (await FindInvitationHandler.FindOpenAsync(context, token, now, cancellationToken) is not { } invitation)
        {
            return OrganizationErrors.InvitationClosed;
        }

        return await AcceptAsync(invitation, userId, verifiesEmail: true, now, cancellationToken);
    }

    /// <summary>
    /// Accepts the invitation for a user with the invited address. Without the link, which proves access to the
    /// inbox, the user's address must already be verified.
    /// </summary>
    internal async Task<Result<Guid>> AcceptAsync(Invitation invitation, Guid userId, bool verifiesEmail, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var user = await context.Users.SingleAsync(user => user.Id == userId, cancellationToken);
        if (user.NormalizedEmail != invitation.NormalizedEmail || (!verifiesEmail && !user.EmailConfirmed))
        {
            return OrganizationErrors.InvitationForSomeoneElse;
        }

        var accepted = invitation.Accept(userId, now);
        if (accepted.IsFailure)
        {
            return accepted.Error;
        }

        if (!user.EmailConfirmed)
        {
            user.MarkEmailVerified(now);
        }

        var roleIds = invitation.Roles.Select(link => link.RoleId).ToList();
        var roles = await context.Roles.Where(role => roleIds.Contains(role.Id)).ToListAsync(cancellationToken);
        var membership = await context.Memberships.Include(membership => membership.Roles)
            .SingleOrDefaultAsync(membership => membership.OrganizationId == invitation.OrganizationId && membership.UserId == userId, cancellationToken);

        if (membership is null)
        {
            membership = Membership.Create(invitation.OrganizationId, userId, now);
            context.Memberships.Add(membership);
            auditLog.Record(AuditActions.MembershipCreated, AuditSubject.User(userId), AddMemberHandler.RolesData(roles.Select(role => role.Key)), organizationId: invitation.OrganizationId);
        }
        else
        {
            var held = membership.Roles.Select(link => link.RoleId).ToList();
            roles.AddRange(await context.Roles.Where(role => held.Contains(role.Id) && !roleIds.Contains(role.Id)).ToListAsync(cancellationToken));
        }

        var assigned = membership.SetRoles(roles, now);
        if (assigned.IsFailure)
        {
            return assigned.Error;
        }

        auditLog.Record(AuditActions.InvitationAccepted, AuditSubject.Invitation(invitation.Id), organizationId: invitation.OrganizationId);
        await context.SaveChangesAsync(cancellationToken);

        return invitation.OrganizationId;
    }
}
