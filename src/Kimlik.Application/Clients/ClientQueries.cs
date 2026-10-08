using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Kimlik.Application.Clients;

/// <summary>Lists clients in creation order. <c>Search</c> matches part of the client ID or display name.</summary>
public sealed record ListClientsQuery(string? Search, string? Cursor, int? Limit);

public sealed class ListClientsHandler(IKimlikDbContext context, IOpenIddictApplicationManager applications)
{
    public async Task<Result<Page<ClientResponse>>> HandleAsync(ListClientsQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        var clients = context.Applications.AsNoTracking();

        if (after is { } afterId)
        {
            clients = clients.Where(client => client.Id > afterId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();

#pragma warning disable CA1304, CA1311, CA1862 // Runs in the database as upper(), where .NET cultures do not apply.
            clients = clients.Where(client => client.ClientId!.ToUpper().Contains(search) || client.DisplayName!.ToUpper().Contains(search));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(clients.OrderBy(client => client.Id), query.Limit, client => client.Id, cancellationToken);
        var roles = await context.RolesOfClientsAsync([.. page.Select(client => client.Id)], cancellationToken);

        var items = new List<ClientResponse>(page.Count);
        foreach (var client in page)
        {
            items.Add(await applications.ToResponseAsync(client, roles[client.Id], cancellationToken));
        }

        return new Page<ClientResponse>(items, nextCursor);
    }
}

public sealed class GetClientHandler(IKimlikDbContext context, IOpenIddictApplicationManager applications)
{
    public async Task<Result<ClientResponse>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await applications.FindByIdAsync(id.ToString(), cancellationToken) is { } application
            ? await applications.ToResponseAsync(context, application, cancellationToken)
            : ClientErrors.NotFound;
}
