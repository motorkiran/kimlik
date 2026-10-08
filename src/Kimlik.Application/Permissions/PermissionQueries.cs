using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Permissions;

/// <summary>Lists permissions in creation order. <c>Search</c> matches part of the key.</summary>
public sealed record ListPermissionsQuery(string? Search, string? Cursor, int? Limit);

public sealed class ListPermissionsHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<PermissionResponse>>> HandleAsync(ListPermissionsQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        var permissions = context.Permissions.AsNoTracking();

        if (after is { } afterId)
        {
            permissions = permissions.Where(permission => permission.Id > afterId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Keys are lowercase by definition.
            var search = query.Search.Trim().ToLowerInvariant();
            permissions = permissions.Where(permission => permission.Key.Contains(search));
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(
            permissions.OrderBy(permission => permission.Id), query.Limit, permission => permission.Id, cancellationToken);

        return new Page<PermissionResponse>([.. page.Select(permission => permission.ToResponse())], nextCursor);
    }
}

public sealed class GetPermissionHandler(IKimlikDbContext context)
{
    public async Task<Result<PermissionResponse>> HandleAsync(Guid permissionId, CancellationToken cancellationToken) =>
        await context.Permissions.AsNoTracking().SingleOrDefaultAsync(permission => permission.Id == permissionId, cancellationToken) is { } permission
            ? permission.ToResponse()
            : PermissionErrors.NotFound;
}
