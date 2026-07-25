using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BaGetter.Core;

/// <summary>
/// Dedicated bounded caches for request-derived search/autocomplete responses.
///
/// Why bounded:
///   - Cache keys are request-derived (query, skip/take, framework, package
///     type, package id) — high-cardinality, so memory could grow without limit.
///
/// Why one cache per response type:
///   - Each type gets its own entry-count budget.
///   - Large search responses can't crowd out small version/dependent lists.
///
/// Sizing (from real production data):
///   - Search:       ~64 KiB/entry
///   - Autocomplete:  ~4 KiB/entry
///   - Versions:      ~2 KiB/entry
///   - Dependents:    ~8 KiB/entry
///   - Default limits total ~160 MiB.
///
/// Eviction: least-recently-used entries are dropped at each cache's cap.
/// </summary>
public sealed class SearchResponseCaches : IDisposable
{
    public SearchResponseCaches(SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Search = Create(options.SearchCacheMaxEntries);
        Autocomplete = Create(options.AutocompleteCacheMaxEntries);
        Versions = Create(options.VersionsCacheMaxEntries);
        Dependents = Create(options.DependentsCacheMaxEntries);
    }

    /// <summary>Cache for <see cref="SearchResponse"/> entries, or null when disabled.</summary>
    public IMemoryCache Search { get; }

    /// <summary>Cache for autocomplete responses, or null when disabled.</summary>
    public IMemoryCache Autocomplete { get; }

    /// <summary>Cache for package-version lists, or null when disabled.</summary>
    public IMemoryCache Versions { get; }

    /// <summary>Cache for dependent-package lists, or null when disabled.</summary>
    public IMemoryCache Dependents { get; }

    private static IMemoryCache Create(int maxEntries)
    {
        // A non-positive limit disables caching for this response type.
        if (maxEntries <= 0)
        {
            return null;
        }

        return new MemoryCache(Options.Create(new MemoryCacheOptions
        {
            // Count-bounded: DatabaseSearchService charges Size = 1 per entry.
            SizeLimit = maxEntries,
        }));
    }

    public void Dispose()
    {
        Search?.Dispose();
        Autocomplete?.Dispose();
        Versions?.Dispose();
        Dependents?.Dispose();
    }
}
