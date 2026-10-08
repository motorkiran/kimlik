using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Common;
using Kimlik.Application.Users;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Organizations;

public sealed class AddMemberHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<MemberResponse>> HandleAsync(Guid organizationId, AddMemberRequest request, CancellationToken cancellationToken)
    {
        if (!await context.Organizations.AnyAsync(organization => organization.Id == organizationId, cancellationToken))
        {
            return OrganizationErrors.NotFound;
        }

        if (!await context.Users.AnyAsync(user => user.Id == request.UserId, cancellationToken))
        {
            return UserErrors.NotFound;
        }

        if (await context.Memberships.AnyAsync(membership => membership.OrganizationId == organizationId && membership.UserId == request.UserId, cancellationToken))
        {
            return OrganizationErrors.AlreadyMember;
        }

        var roles = await RoleSet.ResolveOrganizationAsync(context, request.Roles, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        var now = timeProvider.GetUtcNow();
        var membership = Membership.Create(organizationId, request.UserId, now);
        var assigned = membership.SetRoles(roles.Value, now);
        if (assigned.IsFailure)
        {
            return assigned.Error;
        }

        context.Memberships.Add(membership);
        auditLog.Record(AuditActions.MembershipCreated, AuditSubject.User(request.UserId), RolesData(roles.Value.Select(role => role.Key)), organizationId: organizationId);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return OrganizationErrors.AlreadyMember;
        }

        return await context.ToMemberResponseAsync(membership, cancellationToken);
    }

    internal static Dictionary<string, object?> RolesData(IEnumerable<string> roleKeys) =>
        new() { ["roles"] = roleKeys.Order(StringComparer.Ordinal).ToArray() };
}

/// <summary>Replaces the organization roles of a member. Tokens issued from then on carry the new permissions.</summary>
public sealed class SetMemberRolesHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<MemberResponse>> HandleAsync(Guid organizationId, Guid userId, SetRolesRequest request, CancellationToken cancellationToken)
    {
        if (await FindAsync(context, organizationId, userId, cancellationToken) is not { } membership)
        {
            return OrganizationErrors.MemberNotFound;
        }

        var roles = await RoleSet.ResolveOrganizationAsync(context, request.Roles, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        var unchanged = membership.Roles.Select(link => link.RoleId).ToHashSet().SetEquals(roles.Value.Select(role => role.Id));
        var assigned = membership.SetRoles(roles.Value, timeProvider.GetUtcNow());
        if (assigned.IsFailure)
        {
            return assigned.Error;
        }

        if (!unchanged)
        {
            auditLog.Record(AuditActions.MembershipUpdated, AuditSubject.User(userId), AddMemberHandler.RolesData(roles.Value.Select(role => role.Key)), organizationId: organizationId);
            await context.SaveChangesAsync(cancellationToken);
        }

        return await context.ToMemberResponseAsync(membership, cancellationToken);
    }

    internal static Task<Membership?> FindAsync(IKimlikDbContext context, Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        context.Memberships.Include(membership => membership.Roles)
            .SingleOrDefaultAsync(membership => membership.OrganizationId == organizationId && membership.UserId == userId, cancellationToken);
}

/// <summary>Removes a member. Tokens issued for the organization stop working at their next refresh.</summary>
public sealed class RemoveMemberHandler(IKimlikDbContext context, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        if (await SetMemberRolesHandler.FindAsync(context, organizationId, userId, cancellationToken) is not { } membership)
        {
            return OrganizationErrors.MemberNotFound;
        }

        context.Memberships.Remove(membership);
        auditLog.Record(AuditActions.MembershipDeleted, AuditSubject.User(userId), organizationId: organizationId);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
