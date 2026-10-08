using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Kimlik.Application.ApiResources;

/// <summary>Lists API resources in creation order. <c>Search</c> matches part of the scope.</summary>
public sealed record ListApiResourcesQuery(string? Search, string? Cursor, int? Limit);

public sealed class ListApiResourcesHandler(IKimlikDbContext context, IOpenIddictScopeManager scopes)
{
    public async Task<Result<Page<ApiResourceResponse>>> HandleAsync(ListApiResourcesQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        var resources = context.Scopes.AsNoTracking();

        if (after is { } afterId)
        {
            resources = resources.Where(scope => scope.Id > afterId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            resources = resources.Where(scope => scope.Name!.Contains(search));
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(resources.OrderBy(scope => scope.Id), query.Limit, scope => scope.Id, cancellationToken);

        var items = new List<ApiResourceResponse>(page.Count);
        foreach (var scope in page)
        {
            items.Add(await scopes.ToResponseAsync(scope, cancellationToken));
        }

        return new Page<ApiResourceResponse>(items, nextCursor);
    }
}

public sealed class GetApiResourceHandler(IOpenIddictScopeManager scopes)
{
    public async Task<Result<ApiResourceResponse>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await scopes.FindByIdAsync(id.ToString(), cancellationToken) is { } scope
            ? await scopes.ToResponseAsync(scope, cancellationToken)
            : ApiResourceErrors.NotFound;
}
