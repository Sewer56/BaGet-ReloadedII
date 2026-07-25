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
    private readonly IMemoryCache _cache;
    private readonly SearchOptions _searchOptions;
    private readonly ILogger<DatabaseSearchService> _logger;

    public DatabaseSearchService(
        IContext context,
        IFrameworkCompatibilityService frameworks,
        ISearchResponseBuilder searchBuilder,
        IMemoryCache cache = null,
        IOptions<SearchOptions> searchOptions = null,
        ILogger<DatabaseSearchService> logger = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(frameworks);
        ArgumentNullException.ThrowIfNull(searchBuilder);

        _context = context;
        _frameworks = frameworks;
        _searchBuilder = searchBuilder;
        _cache = cache;
        _searchOptions = searchOptions?.Value ?? new SearchOptions();
        _logger = logger;
    }

    private bool CachingEnabled => _cache is not null && _searchOptions.EnableCache;

    private static TimeSpan Ttl(int seconds, int fallbackSeconds) =>
        TimeSpan.FromSeconds(seconds > 0 ? seconds : fallbackSeconds);

    private async Task<T> GetOrCacheAsync<T>(string cacheKey, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        if (!CachingEnabled)
        {
            return await factory(cancellationToken);
        }

        if (_cache.TryGetValue(cacheKey, out T cached))
        {
            return cached;
        }

        var value = await factory(cancellationToken);

        // PostEvictionCallbacks not needed; a short absolute TTL is sufficient for the
        // eventually-consistent download counts (which are now persisted off-path).
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
        };

        _cache.Set(cacheKey, value, options);
        return value;
    }

    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        // Encode a null framework distinctly from an empty string: null skips framework
        // filtering entirely, while "" resolves to a concrete (non-null) compatible list.
        var cacheKey = $"search|q={request.Query}|skip={request.Skip}|take={request.Take}|pre={request.IncludePrerelease}|sem2={request.IncludeSemVer2}|type={request.PackageType}|fx={request.Framework ?? "<null>"}";

        return GetOrCacheAsync(cacheKey, Ttl(_searchOptions.SearchCacheSeconds, 120), async _ =>
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

        return _searchBuilder.BuildSearch(groupedResults);
        }, cancellationToken);
    }

    public Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken)
    {
        var cacheKey = $"ac|q={request.Query}|skip={request.Skip}|take={request.Take}|pre={request.IncludePrerelease}|sem2={request.IncludeSemVer2}|type={request.PackageType}";

        return GetOrCacheAsync(cacheKey, Ttl(_searchOptions.AutocompleteCacheSeconds, 120), async _ =>
        {
        IQueryable<Package> search = _context.Packages;

        search = ApplySearchQuery(search, request.Query);
        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            request.PackageType,
            frameworks: null);

        var packageIds = await search
            .OrderByDescending(p => p.Downloads)
            .Select(p => p.Id)
            .Distinct()
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(cancellationToken);

        return _searchBuilder.BuildAutocomplete(packageIds);
        }, cancellationToken);
    }

    public Task<AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken)
    {
        var cacheKey = $"vers|id={request.PackageId}|pre={request.IncludePrerelease}|sem2={request.IncludeSemVer2}";

        return GetOrCacheAsync(cacheKey, Ttl(_searchOptions.VersionsCacheSeconds, 60), async _ =>
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

        return GetOrCacheAsync(cacheKey, Ttl(_searchOptions.DependentsCacheSeconds, 60), async _ =>
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
