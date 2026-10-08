using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Common;

/// <summary>
/// Keyset pagination over UUIDv7 keys, which are ordered by creation time. The cursor is the last key of a
/// page, so pages stay stable while items are added and the database seeks straight to the next page.
/// </summary>
public static class Cursor
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static string Encode(Guid lastKey) => lastKey.ToString("N");

    public static bool TryDecode(string? cursor, out Guid? afterKey)
    {
        afterKey = null;
        if (string.IsNullOrEmpty(cursor))
        {
            return true;
        }

        if (Guid.TryParseExact(cursor, "N", out var key))
        {
            afterKey = key;
            return true;
        }

        return false;
    }

    public static int ClampLimit(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

    /// <summary>
    /// Reads one page from a query that is already filtered past the cursor and ordered by key. One extra row
    /// tells whether there is a next page.
    /// </summary>
    internal static async Task<(List<T> Items, string? NextCursor)> ReadPageAsync<T>(
        IQueryable<T> orderedQuery, int? limit, Func<T, Guid> keyOf, CancellationToken cancellationToken)
    {
        var pageSize = ClampLimit(limit);
        var items = await orderedQuery.Take(pageSize + 1).ToListAsync(cancellationToken);

        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(pageSize);
        }

        return (items, hasMore ? Encode(keyOf(items[^1])) : null);
    }
}
