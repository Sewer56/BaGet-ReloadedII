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
}
