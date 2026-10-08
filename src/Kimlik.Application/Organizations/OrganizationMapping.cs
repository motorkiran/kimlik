using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Organizations;

internal static class OrganizationMapping
{
    public static OrganizationResponse ToResponse(this Organization organization) =>
        new(
            organization.Id,
            organization.Name,
            organization.Slug,
            organization.PictureUrl,
            organization.RequireMfa,
            organization.CreatedAt,
            organization.UpdatedAt,
            Metadata.Parse(organization.PublicMetadata),
            Metadata.Parse(organization.PrivateMetadata));

    /// <summary>Describes memberships with the members' email address, name and role keys.</summary>
    public static async Task<List<MemberResponse>> ToMemberResponsesAsync(
        this IKimlikDbContext context, IReadOnlyCollection<Membership> memberships, CancellationToken cancellationToken)
    {
        var membershipIds = memberships.Select(membership => membership.Id).ToList();
        var userIds = memberships.Select(membership => membership.UserId).ToList();

        var users = await context.Users
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Email, user.GivenName, user.FamilyName })
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        var roles = (await context.MembershipRoles
            .Where(link => membershipIds.Contains(link.MembershipId))
            .Join(context.Roles, link => link.RoleId, role => role.Id, (link, role) => new { link.MembershipId, role.Key })
            .ToListAsync(cancellationToken))
            .ToLookup(link => link.MembershipId, link => link.Key);

        return [.. memberships.Select(membership =>
        {
            var user = users[membership.UserId];
            var name = string.Join(' ', new[] { user.GivenName, user.FamilyName }.Where(part => part is not null));
            return new MemberResponse(
                membership.UserId,
                user.Email,
                name.Length > 0 ? name : null,
                [.. roles[membership.Id].Order(StringComparer.Ordinal)],
                membership.CreatedAt);
        })];
    }

    public static async Task<MemberResponse> ToMemberResponseAsync(this IKimlikDbContext context, Membership membership, CancellationToken cancellationToken) =>
        (await context.ToMemberResponsesAsync([membership], cancellationToken))[0];
}
