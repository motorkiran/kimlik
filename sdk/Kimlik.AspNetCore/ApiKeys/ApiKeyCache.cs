using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Kimlik.AspNetCore.ApiKeys;

/// <summary>
/// Kimlik's answers about keys, kept apart from the application's memory cache and limited in size, so that requests
/// with made-up keys can neither fill memory nor push out the application's own entries.
/// </summary>
internal sealed class ApiKeyCache() : MemoryCache(Options.Create(new MemoryCacheOptions { SizeLimit = MaxEntries }))
{
    /// <summary>A few megabytes at most, as an answer takes a few hundred bytes.</summary>
    public const int MaxEntries = 10_000;
}
