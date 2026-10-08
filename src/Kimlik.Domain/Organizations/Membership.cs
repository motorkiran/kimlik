using Kimlik.Domain.Access;
using Kimlik.Domain.Common;

namespace Kimlik.Domain.Organizations;

/// <summary>A user's place in an organization, with the organization roles they hold there.</summary>
public sealed class Membership
{
    private readonly List<MembershipRole> _roles = [];

    // Used by EF Core.
    private Membership()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganizationId { get; private init; }

    public Guid UserId { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<MembershipRole> Roles => _roles;

    public static Membership Create(Guid organizationId, Guid userId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        OrganizationId = organizationId,
        UserId = userId,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>Replaces the member's roles, which must be organization roles.</summary>
    public Result SetRoles(IEnumerable<Role> roles, DateTimeOffset now)
    {
        var wanted = roles.ToList();
        if (!wanted.TrueForAll(role => role.Scope == RoleScope.Organization))
        {
            return OrganizationErrors.GlobalRoleNotAssignable;
        }

        var wantedIds = wanted.Select(role => role.Id).ToHashSet();
        var currentIds = _roles.Select(link => link.RoleId).ToHashSet();
        if (wantedIds.SetEquals(currentIds))
        {
            return Result.Success();
        }

        _roles.RemoveAll(link => !wantedIds.Contains(link.RoleId));
        _roles.AddRange(wantedIds.Except(currentIds).Select(roleId => new MembershipRole(Id, roleId)));
        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>An organization role held through a membership.</summary>
public sealed class MembershipRole(Guid membershipId, Guid roleId)
{
    public Guid MembershipId { get; private init; } = membershipId;

    public Guid RoleId { get; private init; } = roleId;
}
