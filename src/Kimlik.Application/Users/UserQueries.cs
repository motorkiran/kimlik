using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DomainUserStatus = Kimlik.Domain.Users.UserStatus;

namespace Kimlik.Application.Users;

/// <summary>Lists users in creation order. <c>Search</c> matches part of the email address or name, case-insensitively.</summary>
public sealed record ListUsersQuery(string? Search, UserStatus? Status, string? Cursor, int? Limit);

public sealed class ListUsersHandler(IKimlikDbContext context, ILookupNormalizer normalizer)
{
    public async Task<Result<Page<UserResponse>>> HandleAsync(ListUsersQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        if (query.Status is { } requestedStatus && !Enum.IsDefined(requestedStatus))
        {
            return CommonErrors.InvalidParameter("status");
        }

        var limit = Cursor.ClampLimit(query.Limit);
        var users = context.Users.AsNoTracking();

        if (after is { } afterId)
        {
            users = users.Where(user => user.Id > afterId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var email = normalizer.NormalizeEmail(query.Search.Trim());
            var name = query.Search.Trim().ToUpperInvariant();

#pragma warning disable CA1304, CA1311, CA1862 // Runs in the database as upper(), where .NET cultures do not apply.
            users = users.Where(user =>
                user.NormalizedEmail!.Contains(email)
                || user.GivenName!.ToUpper().Contains(name)
                || user.FamilyName!.ToUpper().Contains(name));
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (query.Status is { } status)
        {
            var domainStatus = status == UserStatus.Suspended ? DomainUserStatus.Suspended : DomainUserStatus.Active;
            users = users.Where(user => user.Status == domainStatus);
        }

        // One extra row tells whether there is a next page.
        var page = await users.OrderBy(user => user.Id).Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > limit;
        if (hasMore)
        {
            page.RemoveAt(limit);
        }

        var roles = await context.RolesOfAsync([.. page.Select(user => user.Id)], cancellationToken);

        return new Page<UserResponse>(
            [.. page.Select(user => user.ToResponse(roles[user.Id]))],
            hasMore ? Cursor.Encode(page[^1].Id) : null);
    }
}

public sealed class GetUserHandler(IKimlikDbContext context)
{
    public async Task<Result<UserResponse>> HandleAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken) is { } user
            ? await context.ToResponseAsync(user, cancellationToken)
            : UserErrors.NotFound;
}
