using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;
using Microsoft.EntityFrameworkCore;
using DomainUser = Kimlik.Domain.Users.User;
using DomainUserStatus = Kimlik.Domain.Users.UserStatus;

namespace Kimlik.Application.Users;

internal static class UserMapping
{
    public static UserResponse ToResponse(this DomainUser user, IEnumerable<string> roles) => new(
        user.Id,
        user.Email,
        user.EmailConfirmed,
        user.GivenName,
        user.FamilyName,
        user.Name,
        user.Locale,
        user.Status == DomainUserStatus.Suspended ? UserStatus.Suspended : UserStatus.Active,
        [.. roles.Order(StringComparer.Ordinal)],
        user.CreatedAt,
        user.UpdatedAt,
        user.LastSignInAt,
        Metadata.Parse(user.PublicMetadata),
        Metadata.Parse(user.PrivateMetadata));

    public static ProfileResponse ToProfile(this DomainUser user, IEnumerable<string> roles) => new(
        user.Id,
        user.Email,
        user.EmailConfirmed,
        user.GivenName,
        user.FamilyName,
        user.Name,
        user.Locale,
        [.. roles.Order(StringComparer.Ordinal)],
        user.CreatedAt,
        user.UpdatedAt,
        user.LastSignInAt,
        Metadata.Parse(user.PublicMetadata));

    /// <summary>The global role keys of each of the given users.</summary>
    public static async Task<ILookup<Guid, string>> RolesOfAsync(this IKimlikDbContext context, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var assignments = await context.UserRoles
            .Where(assignment => userIds.Contains(assignment.UserId))
            .Join(context.Roles, assignment => assignment.RoleId, role => role.Id, (assignment, role) => new { assignment.UserId, role.Key })
            .ToListAsync(cancellationToken);

        return assignments.ToLookup(assignment => assignment.UserId, assignment => assignment.Key);
    }

    public static async Task<UserResponse> ToResponseAsync(this IKimlikDbContext context, DomainUser user, CancellationToken cancellationToken) =>
        user.ToResponse((await context.RolesOfAsync([user.Id], cancellationToken))[user.Id]);

    public static async Task<ProfileResponse> ToProfileAsync(this IKimlikDbContext context, DomainUser user, CancellationToken cancellationToken) =>
        user.ToProfile((await context.RolesOfAsync([user.Id], cancellationToken))[user.Id]);
}
