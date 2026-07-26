using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGetter.Core;

/// <summary>
/// A <see cref="BackgroundService"/> that coalesces package-download increments
/// in memory and flushes them to the database in batches.
/// </summary>
public class BackgroundDownloadCounter : BackgroundService, IDownloadCounter
{
    // Baked-in production values: flushing on a fixed interval gives predictable
    // staleness under bursty traffic, and the pending cap bounds memory if the
    // database is unavailable.
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(60);
    private const int MaxPending = 50000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundDownloadCounter> _logger;

    // (packageId, normalized version) -> pending increment count.
    private readonly ConcurrentDictionary<(string Id, string NormalizedVersionString), int> _pending
        = new ConcurrentDictionary<(string Id, string NormalizedVersionString), int>();

    public BackgroundDownloadCounter(
        IServiceScopeFactory scopeFactory,
        ILogger<BackgroundDownloadCounter> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Enqueue(string packageId, NuGetVersion version)
    {
        if (string.IsNullOrEmpty(packageId) || version is null)
        {
            return;
        }

        var key = (Id: packageId, NormalizedVersionString: version.ToNormalizedString());

        // Back-pressure: if the buffer is saturated (e.g. the database is down), drop
        // the increment rather than growing memory without bound. This matches the
        // legacy behaviour's worst case (a lost increment) while keeping the process alive.
        if (_pending.Count >= MaxPending)
        {
            _logger.LogWarning("Download-count buffer saturated ({Count} entries); dropping increment for {Id} {Version}.",
                _pending.Count, packageId, key.NormalizedVersionString);
            return;
        }

        _pending.AddOrUpdate(key, 1, (_, current) => current + 1);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Background download counter started (flush every {Seconds}s).",
            FlushInterval.TotalSeconds);

        using var timer = new PeriodicTimer(FlushInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // WaitForNextTickAsync throws OperationCanceledException when the token
                // cancels, which the catch below swallows. Flushes are driven by this
                // interval tick only; there is no pending-count early-flush path.
                await timer.WaitForNextTickAsync(stoppingToken);
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background download counter loop terminated unexpectedly.");
        }

        _logger.LogInformation("Background download counter stopped.");
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Flush anything still buffered so queued increments are not lost on a
        // graceful shutdown / deployment rollout.
        try
        {
            await FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush pending download increments on shutdown.");
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        // Snapshot & clear into a pre-sized list. The buffered keys are already
        // unique, so a flat list avoids the per-insertion hash lookups a
        // Dictionary would pay. Increments that arrive during the flush land in a
        // fresh buffer and are persisted on the next cycle; concurrent enqueues
        // can also grow the buffer past the captured count mid-copy, in which case
        // the list simply grows past its initial capacity.
        var snapshot = new List<DownloadIncrement>(_pending.Count);
        foreach (var pair in _pending)
        {
            if (_pending.TryRemove(pair.Key, out var value))
            {
                snapshot.Add(new DownloadIncrement(pair.Key, value));
            }
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
            await database.IncrementDownloadsAsync(snapshot, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Re-queue before re-throwing on shutdown.
            Requeue(snapshot);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist {Count} download increments; re-queuing for the next flush.",
                snapshot.Count);
            Requeue(snapshot);
        }
    }

    private void Requeue(IReadOnlyList<DownloadIncrement> snapshot)
    {
        foreach (var entry in snapshot)
        {
            if (_pending.Count >= MaxPending)
            {
                _logger.LogWarning(
                    "Download-count buffer saturated ({Count} entries); dropping remaining re-queued increments.",
                    _pending.Count);
                break;
            }

            _pending.AddOrUpdate(entry.Key, entry.Delta, (_, current) => current + entry.Delta);
        }
    }
}

/// <summary>
/// A single coalesced download-count increment: the number of new downloads
/// (<see cref="Delta"/>) to add for one (package id, normalized version) key.
/// Keys are unique within a batch, so batches are passed as flat lists rather
/// than dictionaries.
/// </summary>
public readonly record struct DownloadIncrement(
    (string Id, string NormalizedVersionString) Key,
    int Delta);
