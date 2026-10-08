using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Organizations;

/// <summary>Lists organizations in creation order. <c>Search</c> matches part of the name or slug.</summary>
public sealed record ListOrganizationsQuery(string? Search, string? Cursor, int? Limit);

public sealed class ListOrganizationsHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<OrganizationResponse>>> HandleAsync(ListOrganizationsQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        var organizations = context.Organizations.AsNoTracking();

        if (after is { } afterId)
        {
            organizations = organizations.Where(organization => organization.Id > afterId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var slug = query.Search.Trim().ToLowerInvariant();
            var name = query.Search.Trim().ToUpperInvariant();

#pragma warning disable CA1304, CA1311, CA1862 // Runs in the database as upper(), where .NET cultures do not apply.
            organizations = organizations.Where(organization => organization.Slug.Contains(slug) || organization.Name.ToUpper().Contains(name));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(
            organizations.OrderBy(organization => organization.Id), query.Limit, organization => organization.Id, cancellationToken);

        return new Page<OrganizationResponse>([.. page.Select(organization => organization.ToResponse())], nextCursor);
    }
}

public sealed class GetOrganizationHandler(IKimlikDbContext context)
{
    public async Task<Result<OrganizationResponse>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Organizations.AsNoTracking().SingleOrDefaultAsync(organization => organization.Id == id, cancellationToken) is { } organization
            ? organization.ToResponse()
            : OrganizationErrors.NotFound;
}

/// <summary>Lists the members of an organization in the order they joined.</summary>
public sealed record ListMembersQuery(Guid OrganizationId, string? Cursor, int? Limit);

public sealed class ListMembersHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<MemberResponse>>> HandleAsync(ListMembersQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        if (!await context.Organizations.AnyAsync(organization => organization.Id == query.OrganizationId, cancellationToken))
        {
            return OrganizationErrors.NotFound;
        }

        var memberships = context.Memberships.AsNoTracking().Where(membership => membership.OrganizationId == query.OrganizationId);
        if (after is { } afterId)
        {
            memberships = memberships.Where(membership => membership.Id > afterId);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(
            memberships.OrderBy(membership => membership.Id), query.Limit, membership => membership.Id, cancellationToken);

        return new Page<MemberResponse>(await context.ToMemberResponsesAsync(page, cancellationToken), nextCursor);
    }
}
