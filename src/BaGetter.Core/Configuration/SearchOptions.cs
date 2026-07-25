namespace BaGetter.Core;

public class SearchOptions
{
    public string Type { get; set; }

    /// <summary>
    /// Whether to cache search/autocomplete responses in-memory. Search and
    /// autocomplete are read-heavy and change infrequently, so a short-lived
    /// cache dramatically reduces database load under burst traffic.
    /// </summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>
    /// Time-to-live, in seconds, for cached search responses. Search is the most
    /// expensive query and tolerates staleness, so it gets a long TTL.
    /// </summary>
    public int SearchCacheSeconds { get; set; } = 120;

    /// <summary>
    /// Time-to-live, in seconds, for cached autocomplete responses.
    /// </summary>
    public int AutocompleteCacheSeconds { get; set; } = 120;

    /// <summary>
    /// Time-to-live, in seconds, for cached package-version lists. Kept short:
    /// users commonly check versions right after a push.
    /// </summary>
    public int VersionsCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Time-to-live, in seconds, for cached dependent-package lists.
    /// </summary>
    public int DependentsCacheSeconds { get; set; } = 60;

    // Per-type cache entry limits. Sizes estimated from real production feed data
    // (avg 6.2 versions/package, ~80-char descriptions, take=20 pages); defaults
    // total ~160 MiB. Set a limit to 0 to disable caching for that type.

    /// <summary>Max cached search responses: 2048 × ~64 KiB ≈ 128 MiB.</summary>
    public int SearchCacheMaxEntries { get; set; } = 2048;

    /// <summary>Max cached autocomplete responses: 4096 × ~4 KiB ≈ 16 MiB.</summary>
    public int AutocompleteCacheMaxEntries { get; set; } = 4096;

    /// <summary>Max cached package-version lists: 4096 × ~2 KiB ≈ 8 MiB.</summary>
    public int VersionsCacheMaxEntries { get; set; } = 4096;

    /// <summary>Max cached dependent-package lists: 1024 × ~8 KiB ≈ 8 MiB.</summary>
    public int DependentsCacheMaxEntries { get; set; } = 1024;
}
