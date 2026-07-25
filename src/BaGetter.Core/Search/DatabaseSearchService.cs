using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Protocol.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGetter.Core;

public class DatabaseSearchService : ISearchService
{
    private readonly IContext _context;
    private readonly IFrameworkCompatibilityService _frameworks;
    private readonly ISearchResponseBuilder _searchBuilder;
    private readonly SearchResponseCaches _caches;
    private readonly SearchOptions _searchOptions;
    private readonly ILogger<DatabaseSearchService> _logger;

    public DatabaseSearchService(
        IContext context,
        IFrameworkCompatibilityService frameworks,
        ISearchResponseBuilder searchBuilder,
        SearchResponseCaches caches = null,
        IOptions<SearchOptions> searchOptions = null,
        ILogger<DatabaseSearchService> logger = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(frameworks);
        ArgumentNullException.ThrowIfNull(searchBuilder);

        _context = context;
        _frameworks = frameworks;
        _searchBuilder = searchBuilder;
        _caches = caches;
        _searchOptions = searchOptions?.Value ?? new SearchOptions();
        _logger = logger;
    }

    private bool CachingEnabled => _caches is not null && _searchOptions.EnableCache;

    private static TimeSpan Ttl(int seconds, int fallbackSeconds) =>
        TimeSpan.FromSeconds(seconds > 0 ? seconds : fallbackSeconds);

    private async Task<T> GetOrCacheAsync<T>(IMemoryCache cache, string cacheKey, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        if (!CachingEnabled || cache is null)
        {
            return await factory(cancellationToken);
        }

        if (cache.TryGetValue(cacheKey, out T cached))
        {
            return cached;
        }

        var value = await factory(cancellationToken);

        // PostEvictionCallbacks not needed; a short absolute TTL is sufficient for the
        // eventually-consistent download counts (which are now persisted off-path).
        // Size = 1: each cache is count-bounded via its SizeLimit (see SearchResponseCaches).
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Size = 1,
        };

        cache.Set(cacheKey, value, options);
        return value;
    }

    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        request = NormalizePaging(request);
        // Encode a null framework distinctly from an empty string: null skips framework
        // filtering entirely, while "" resolves to a concrete (non-null) compatible list.
        var cacheKey = $"search|q={request.Query}|skip={request.Skip}|take={request.Take}|pre={request.IncludePrerelease}|sem2={request.IncludeSemVer2}|type={request.PackageType}|fx={request.Framework ?? "<null>"}";

        return GetOrCacheAsync(_caches?.Search, cacheKey, Ttl(_searchOptions.SearchCacheSeconds, 120), async _ =>
        {
        var frameworks = GetCompatibleFrameworksOrNull(request.Framework);

        IQueryable<Package> search = _context.Packages;
        search = ApplySearchQuery(search, request.Query);
        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            request.PackageType,
            frameworks);

        // True total match count (distinct package IDs), ignoring Skip/Take.
        // Without this, TotalHits would equal the page size and clients could not
        // distinguish "more results" from "last page" — see nuget.org parity note
        // in the NormalizePaging remarks: clients stop on a short/empty page, but
        // some return totalHits for display, so it must reflect the real total.
        var totalHits = await search
            .Select(p => p.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        var packageIds = search
            .Select(p => p.Id)
            .Distinct()
            .OrderBy(id => id)
            .Skip(request.Skip)
            .Take(request.Take);

        // This query MUST fetch all versions for each package that matches the search,
        // otherwise the results for a package's latest version may be incorrect.
        // If possible, we'll find all these packages in a single query by matching
        // the package IDs in a subquery. Otherwise, run two queries:
        //   1. Find the package IDs that match the search
        //   2. Find all package versions for these package IDs
        if (_context.SupportsLimitInSubqueries)
        {
            search = _context.Packages.Where(p => packageIds.Contains(p.Id));
        }
        else
        {
            var packageIdResults = await packageIds.ToListAsync(cancellationToken);

            search = _context.Packages.Where(p => packageIdResults.Contains(p.Id));
        }

        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            request.PackageType,
            frameworks);

        var results = await search.ToListAsync(cancellationToken);
        var groupedResults = results
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PackageRegistration(group.Key, group.ToList()))
            .ToList();

        return _searchBuilder.BuildSearch(groupedResults, totalHits);
        }, cancellationToken);
    }

    public Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken)
    {
        request = NormalizePaging(request);
        var cacheKey = $"ac|q={request.Query}|skip={request.Skip}|take={request.Take}|pre={request.IncludePrerelease}|sem2={request.IncludeSemVer2}|type={request.PackageType}";

        return GetOrCacheAsync(_caches?.Autocomplete, cacheKey, Ttl(_searchOptions.AutocompleteCacheSeconds, 120), async _ =>
        {
        IQueryable<Package> search = _context.Packages;

        search = ApplySearchQuery(search, request.Query);
        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            request.PackageType,
            frameworks: null);

        // True total match count (distinct package IDs), ignoring Skip/Take.
        // See SearchAsync for rationale.
        var totalHits = await search
            .Select(p => p.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        var packageIds = await search
            .OrderByDescending(p => p.Downloads)
            .Select(p => p.Id)
            .Distinct()
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(cancellationToken);

        return _searchBuilder.BuildAutocomplete(packageIds, totalHits);
        }, cancellationToken);
    }

    public Task<AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken)
    {
        var cacheKey = $"vers|id={request.PackageId}|pre={request.IncludePrerelease}|sem2={request.IncludeSemVer2}";

        return GetOrCacheAsync(_caches?.Versions, cacheKey, Ttl(_searchOptions.VersionsCacheSeconds, 60), async _ =>
        {
        var packageId = request.PackageId.ToLower();
        var search = _context
            .Packages
            .Where(p => p.Id.ToLower().Equals(packageId));

        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            packageType: null,
            frameworks: null);

        var packageVersions = await search
            .Select(p => p.NormalizedVersionString)
            .ToListAsync(cancellationToken);

        return _searchBuilder.BuildAutocomplete(packageVersions);
        }, cancellationToken);
    }

    public Task<DependentsResponse> FindDependentsAsync(string packageId, CancellationToken cancellationToken)
    {
        var cacheKey = $"deps|id={packageId}";

        return GetOrCacheAsync(_caches?.Dependents, cacheKey, Ttl(_searchOptions.DependentsCacheSeconds, 60), async _ =>
        {
        var dependents = await _context
            .Packages
            .Where(p => p.Listed)
            .OrderByDescending(p => p.Downloads)
            .Where(p => p.Dependencies.Any(d => d.Id == packageId))
            .Take(20)
            .Select(r => new PackageDependent
            {
                Id = r.Id,
                Description = r.Description,
                TotalDownloads = r.Downloads
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        return _searchBuilder.BuildDependents(dependents);
        }, cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons", Justification = "Not for EF queries")]
    private static IQueryable<Package> ApplySearchQuery(IQueryable<Package> query, string search)
    {
        if (string.IsNullOrEmpty(search))
        {
            return query;
        }

        search = search.ToLowerInvariant();

        return query.Where(p =>
            p.Id.ToLower().Contains(search) ||
            (p.Title != null && p.Title.ToLower().Contains(search)) ||
            (p.TagsString != null && p.TagsString.ToLower().Contains(search)));
    }

    /// <summary>
    /// Clamp the page size so a single request cannot force an unbounded query or cache
    /// an enormous response. <c>Skip</c> is left unbounded so clients that paginate to
    /// exhaustion (e.g. <c>skip += take</c> until an empty page) can reach every result.
    /// </summary>
    /// <remarks>
    /// The cap is set to 1000 to fit downstream consumers that page in steps of 1000
    /// (e.g. Reloaded-II's <c>NuGetPackageProvider</c> uses <c>take=1000</c>). A higher
    /// cap would still loop correctly because the client stops on a short/empty page;
    /// a lower cap would silently truncate and (for clients that enable "next page" only
    /// when <c>count &gt;= take</c>) break pagination entirely.
    /// </remarks>
    private const int MaxSearchTake = 1000;
    private const int DefaultSearchTake = 20;

    private static SearchRequest NormalizePaging(SearchRequest request)
    {
        var take = request.Take <= 0 ? DefaultSearchTake : Math.Min(request.Take, MaxSearchTake);
        var skip = Math.Max(request.Skip, 0);

        if (take == request.Take && skip == request.Skip)
        {
            return request;
        }

        return new SearchRequest
        {
            Query = request.Query,
            Skip = skip,
            Take = take,
            IncludePrerelease = request.IncludePrerelease,
            IncludeSemVer2 = request.IncludeSemVer2,
            PackageType = request.PackageType,
            Framework = request.Framework,
        };
    }

    private static AutocompleteRequest NormalizePaging(AutocompleteRequest request)
    {
        var take = request.Take <= 0 ? DefaultSearchTake : Math.Min(request.Take, MaxSearchTake);
        var skip = Math.Max(request.Skip, 0);

        if (take == request.Take && skip == request.Skip)
        {
            return request;
        }

        return new AutocompleteRequest
        {
            Query = request.Query,
            Skip = skip,
            Take = take,
            IncludePrerelease = request.IncludePrerelease,
            IncludeSemVer2 = request.IncludeSemVer2,
            PackageType = request.PackageType,
        };
    }


    private static IQueryable<Package> ApplySearchFilters(
        IQueryable<Package> query,
        bool includePrerelease,
        bool includeSemVer2,
        string packageType,
        IReadOnlyList<string> frameworks)
    {
        if (!includePrerelease)
        {
            query = query.Where(p => !p.IsPrerelease);
        }

        if (!includeSemVer2)
        {
            query = query.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
        }

        if (!string.IsNullOrEmpty(packageType))
        {
            query = query.Where(p => p.PackageTypes.Any(t => t.Name == packageType));
        }

        if (frameworks != null)
        {
            query = query.Where(p => p.TargetFrameworks.Any(f => frameworks.Contains(f.Moniker)));
        }

        return query.Where(p => p.Listed);
    }

    private IReadOnlyList<string> GetCompatibleFrameworksOrNull(string framework)
    {
        if (framework == null) return null;

        return _frameworks.FindAllCompatibleFrameworks(framework);
    }
}
