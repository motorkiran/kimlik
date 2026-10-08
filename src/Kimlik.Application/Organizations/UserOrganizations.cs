using Kimlik.Application.Abstractions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Organizations;

/// <summary>The organizations a user belongs to, for choosing the organization a token acts in.</summary>
public sealed class UserOrganizations(IKimlikDbContext context)
{
    /// <summary>The organization named by ID or slug, if the user is a member of it.</summary>
    public async Task<Result<OrganizationResponse>> FindAsync(Guid userId, string reference, CancellationToken cancellationToken)
    {
        var organizations = Guid.TryParse(reference, out var id)
            ? context.Organizations.Where(organization => organization.Id == id)
            : context.Organizations.Where(organization => organization.Slug == reference);

        var found = await organizations
            .Where(organization => context.Memberships.Any(membership => membership.OrganizationId == organization.Id && membership.UserId == userId))
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        return found is null ? OrganizationErrors.NotAMember : found.ToResponse();
    }

    public async Task<IReadOnlyList<OrganizationResponse>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        [.. (await context.Organizations
            .Where(organization => context.Memberships.Any(membership => membership.OrganizationId == organization.Id && membership.UserId == userId))
            .OrderBy(organization => organization.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .Select(organization => organization.ToResponse())];
}
