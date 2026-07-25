using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

namespace BaGetter.Core;

public class PackageDatabase : IPackageDatabase
{
    private readonly IContext _context;

    public PackageDatabase(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PackageAddResult> AddAsync(Package package, CancellationToken cancellationToken)
    {
        try
        {
            _context.Packages.Add(package);

            await _context.SaveChangesAsync(cancellationToken);

            return PackageAddResult.Success;
        }
        catch (DbUpdateException e)
            when (_context.IsUniqueConstraintViolationException(e))
        {
            return PackageAddResult.PackageAlreadyExists;
        }
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
    {
        return await _context
            .Packages
            .Where(p => p.Id == id)
            .AnyAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await _context
            .Packages
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Package>> FindAsync(string id, bool includeUnlisted, CancellationToken cancellationToken)
    {
        var query = _context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.PackageTypes)
            .Include(p => p.TargetFrameworks)
            .Where(p => p.Id == id);

        if (!includeUnlisted)
        {
            query = query.Where(p => p.Listed);
        }

        return (await query.AsSingleQuery().ToListAsync(cancellationToken)).AsReadOnly();
    }

    public Task<Package> FindOrNullAsync(
        string id,
        NuGetVersion version,
        bool includeUnlisted,
        CancellationToken cancellationToken)
    {
        var query = _context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.TargetFrameworks)
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString());

        if (!includeUnlisted)
        {
            query = query.Where(p => p.Listed);
        }

        return query.AsSingleQuery().FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> UnlistPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return TryUpdatePackageAsync(id, version, p => p.Listed = false, cancellationToken);
    }

    public Task<bool> RelistPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return TryUpdatePackageAsync(id, version, p => p.Listed = true, cancellationToken);
    }

    public async Task IncrementDownloadsAsync(
        List<DownloadIncrement> increments,
        CancellationToken cancellationToken)
    {
        if (increments is null || increments.Count == 0)
        {
            return;
        }

        // Apply all increments inside a single transaction. Each increment is an atomic,
        // provider-agnostic `Downloads = Downloads + @delta` UPDATE that does not load
        // the row into the change tracker, so the write lock is held for the shortest
        // time possible and many increments collapse into one commit.
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        foreach (var (key, delta) in increments)
        {
            if (delta == 0)
            {
                continue;
            }

            await _context.Packages
                .Where(p => p.Id == key.Id && p.NormalizedVersionString == key.NormalizedVersionString)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(p => p.Downloads, p => p.Downloads + delta),
                    cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> HardDeletePackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        var package = await _context.Packages
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .Include(p => p.Dependencies)
            .Include(p => p.TargetFrameworks)
            .AsSingleQuery()
            .FirstOrDefaultAsync(cancellationToken);

        if (package == null)
        {
            return false;
        }

        _context.Packages.Remove(package);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<bool> TryUpdatePackageAsync(
        string id,
        NuGetVersion version,
        Action<Package> action,
        CancellationToken cancellationToken)
    {
        var package = await _context.Packages
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .FirstOrDefaultAsync(cancellationToken);

        if (package != null)
        {
            action(package);
            await _context.SaveChangesAsync(cancellationToken);

            return true;
        }

        return false;
    }
}
