using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Roles;

/// <summary>Lists roles in creation order. <c>Search</c> matches part of the key or name.</summary>
public sealed record ListRolesQuery(string? Search, RoleScope? Scope, string? Cursor, int? Limit);

public sealed class ListRolesHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<RoleResponse>>> HandleAsync(ListRolesQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        if (query.Scope is { } requestedScope && !Enum.IsDefined(requestedScope))
        {
            return CommonErrors.InvalidParameter("scope");
        }

        var roles = context.Roles.AsNoTracking();

        if (after is { } afterId)
        {
            roles = roles.Where(role => role.Id > afterId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var key = query.Search.Trim().ToLowerInvariant();
            var name = query.Search.Trim().ToUpperInvariant();

#pragma warning disable CA1304, CA1311, CA1862 // Runs in the database as upper(), where .NET cultures do not apply.
            roles = roles.Where(role => role.Key.Contains(key) || role.Name.ToUpper().Contains(name));
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (query.Scope is { } scope)
        {
            var domainScope = scope.ToDomain();
            roles = roles.Where(role => role.Scope == domainScope);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(roles.OrderBy(role => role.Id), query.Limit, role => role.Id, cancellationToken);
        var permissions = await context.PermissionsOfAsync([.. page.Select(role => role.Id)], cancellationToken);

        return new Page<RoleResponse>([.. page.Select(role => role.ToResponse(permissions[role.Id]))], nextCursor);
    }
}

public sealed class GetRoleHandler(IKimlikDbContext context)
{
    public async Task<Result<RoleResponse>> HandleAsync(Guid roleId, CancellationToken cancellationToken) =>
        await context.Roles.AsNoTracking().SingleOrDefaultAsync(role => role.Id == roleId, cancellationToken) is { } role
            ? await context.ToResponseAsync(role, cancellationToken)
            : RoleErrors.NotFound;
}
