using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DomainInvitationStatus = Kimlik.Domain.Organizations.InvitationStatus;
using InvitationStatus = Kimlik.Contracts.Management.InvitationStatus;

namespace Kimlik.Application.Organizations;

internal static class InvitationMapping
{
    public static async Task<List<InvitationResponse>> ToResponsesAsync(
        this IKimlikDbContext context, IReadOnlyCollection<Invitation> invitations, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = invitations.Select(invitation => invitation.Id).ToList();
        var roles = (await context.Invitations
            .Where(invitation => ids.Contains(invitation.Id))
            .SelectMany(invitation => invitation.Roles)
            .Join(context.Roles, link => link.RoleId, role => role.Id, (link, role) => new { link.InvitationId, role.Key })
            .ToListAsync(cancellationToken))
            .ToLookup(link => link.InvitationId, link => link.Key);

        return [.. invitations.Select(invitation => new InvitationResponse(
            invitation.Id,
            invitation.Email,
            [.. roles[invitation.Id].Order(StringComparer.Ordinal)],
            StatusAt(invitation, now),
            invitation.ExpiresAt,
            invitation.CreatedAt))];
    }

    private static InvitationStatus StatusAt(Invitation invitation, DateTimeOffset now) => invitation.Status switch
    {
        DomainInvitationStatus.Accepted => InvitationStatus.Accepted,
        DomainInvitationStatus.Revoked => InvitationStatus.Revoked,
        _ => invitation.IsOpenAt(now) ? InvitationStatus.Pending : InvitationStatus.Expired,
    };
}

/// <summary>Lists an organization's invitations, newest first.</summary>
public sealed record ListInvitationsQuery(Guid OrganizationId, string? Cursor, int? Limit);

public sealed class ListInvitationsHandler(IKimlikDbContext context, TimeProvider timeProvider)
{
    public async Task<Result<Page<InvitationResponse>>> HandleAsync(ListInvitationsQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var before))
        {
            return CommonErrors.InvalidCursor;
        }

        if (!await context.Organizations.AnyAsync(organization => organization.Id == query.OrganizationId, cancellationToken))
        {
            return OrganizationErrors.NotFound;
        }

        var invitations = context.Invitations.AsNoTracking().Where(invitation => invitation.OrganizationId == query.OrganizationId);
        if (before is { } beforeId)
        {
            invitations = invitations.Where(invitation => invitation.Id < beforeId);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(
            invitations.OrderByDescending(invitation => invitation.Id), query.Limit, invitation => invitation.Id, cancellationToken);

        return new Page<InvitationResponse>(await context.ToResponsesAsync(page, timeProvider.GetUtcNow(), cancellationToken), nextCursor);
    }
}

/// <summary>Invites an email address to an organization and sends the invitation.</summary>
public sealed class CreateInvitationHandler(
    IKimlikDbContext context,
    ILookupNormalizer normalizer,
    IOutbox outbox,
    IAuditLog auditLog,
    IOptions<OrganizationOptions> options,
    TimeProvider timeProvider)
{
    public async Task<Result<InvitationResponse>> HandleAsync(Guid organizationId, CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        if (!await context.Organizations.AnyAsync(organization => organization.Id == organizationId, cancellationToken))
        {
            return OrganizationErrors.NotFound;
        }

        var email = normalizer.NormalizeEmail(request.Email.Trim());
        if (await context.Memberships.AnyAsync(
            membership => membership.OrganizationId == organizationId && context.Users.Any(user => user.Id == membership.UserId && user.NormalizedEmail == email),
            cancellationToken))
        {
            return OrganizationErrors.AlreadyMember;
        }

        var now = timeProvider.GetUtcNow();
        var pending = await context.Invitations
            .Where(invitation => invitation.OrganizationId == organizationId && invitation.NormalizedEmail == email && invitation.Status == DomainInvitationStatus.Pending)
            .ToListAsync(cancellationToken);

        if (pending.Exists(invitation => invitation.IsOpenAt(now)))
        {
            return OrganizationErrors.InvitationPending;
        }

        var roles = await RoleSet.ResolveOrganizationAsync(context, request.Roles, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        var created = Invitation.Create(organizationId, request.Email, email, roles.Value, now + options.Value.InvitationLifetime, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        // Expired invitations to the same address make way for the new one.
        pending.ForEach(expired => expired.Revoke(now));

        var invitation = created.Value;
        context.Invitations.Add(invitation);
        outbox.Enqueue(new SendInvitation(invitation.Id));
        auditLog.Record(AuditActions.InvitationCreated, AuditSubject.Invitation(invitation.Id), AddMemberHandler.RolesData(roles.Value.Select(role => role.Key)), organizationId: organizationId);
        await context.SaveChangesAsync(cancellationToken);

        return (await context.ToResponsesAsync([invitation], now, cancellationToken))[0];
    }
}

/// <summary>Sends a pending invitation again with a new link and a new expiry; the earlier link stops working.</summary>
public sealed class ResendInvitationHandler(IKimlikDbContext context, IOutbox outbox, IAuditLog auditLog, IOptions<OrganizationOptions> options, TimeProvider timeProvider)
{
    public async Task<Result<InvitationResponse>> HandleAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken)
    {
        if (await FindAsync(context, organizationId, invitationId, cancellationToken) is not { } invitation)
        {
            return OrganizationErrors.InvitationNotFound;
        }

        var now = timeProvider.GetUtcNow();
        var renewed = invitation.Renew(now + options.Value.InvitationLifetime, now);
        if (renewed.IsFailure)
        {
            return renewed.Error;
        }

        outbox.Enqueue(new SendInvitation(invitation.Id));
        auditLog.Record(AuditActions.InvitationResent, AuditSubject.Invitation(invitation.Id), organizationId: organizationId);
        await context.SaveChangesAsync(cancellationToken);

        return (await context.ToResponsesAsync([invitation], now, cancellationToken))[0];
    }

    internal static Task<Invitation?> FindAsync(IKimlikDbContext context, Guid organizationId, Guid invitationId, CancellationToken cancellationToken) =>
        context.Invitations.SingleOrDefaultAsync(invitation => invitation.Id == invitationId && invitation.OrganizationId == organizationId, cancellationToken);
}

public sealed class RevokeInvitationHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken)
    {
        if (await ResendInvitationHandler.FindAsync(context, organizationId, invitationId, cancellationToken) is not { } invitation)
        {
            return OrganizationErrors.InvitationNotFound;
        }

        var revoked = invitation.Revoke(timeProvider.GetUtcNow());
        if (revoked.IsFailure)
        {
            return revoked;
        }

        auditLog.Record(AuditActions.InvitationRevoked, AuditSubject.Invitation(invitation.Id), organizationId: organizationId);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
