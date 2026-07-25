using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGetter.Core.Tests.Services;

// The counter flushes on a baked-in 5s periodic interval and on graceful shutdown.
// These tests never start the hosted loop (no StartAsync), so the real timer never
// ticks; the only flush path exercised is the public StopAsync override, which calls
// the same FlushAsync the interval uses. This keeps the tests deterministic without
// shortening the production interval or introducing a runtime flush configuration
// knob.
public class BackgroundDownloadCounterTests
{
    private const string PackageId = "TestPackage";
    private static readonly NuGetVersion PackageVersion = new NuGetVersion("1.2.3");

    private readonly Mock<IPackageDatabase> _db;
    private readonly List<List<DownloadIncrement>> _calls;
    private readonly BackgroundDownloadCounter _target;

    public BackgroundDownloadCounterTests()
    {
        _db = new Mock<IPackageDatabase>();
        _calls = new List<List<DownloadIncrement>>();

        _db.Setup(d => d.IncrementDownloadsAsync(
                It.IsAny<List<DownloadIncrement>>(),
                It.IsAny<CancellationToken>()))
            .Callback<List<DownloadIncrement>, CancellationToken>(
                (snapshot, _) => _calls.Add(snapshot))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(_db.Object);
        var scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        _target = new BackgroundDownloadCounter(scopeFactory, Mock.Of<ILogger<BackgroundDownloadCounter>>());
    }

    private static (string Id, string NormalizedVersionString) Key(string id, NuGetVersion version)
        => (id, version.ToNormalizedString());

    // Core behavior: StopAsync applies the coalesced snapshot and clears the buffer.

    [Fact]
    public async Task StopAsync_AppliesCoalescedSnapshot_AndClearsBuffer()
    {
        // Arrange - enqueuing the same (id, version) twice coalesces into a single
        // buffered entry whose delta is the sum of the increments.
        _target.Enqueue(PackageId, PackageVersion);
        _target.Enqueue(PackageId, PackageVersion);

        // Act - StopAsync drives the one and only flush path (the same FlushAsync the
        // periodic interval calls).
        await _target.StopAsync(CancellationToken.None);

        // Assert
        Assert.Single(_calls);
        var snapshot = _calls[0];
        var entry = Assert.Single(snapshot);
        Assert.Equal(Key(PackageId, PackageVersion), entry.Key);
        Assert.Equal(2, entry.Delta);

        // Act again - a second flush finds an empty buffer (the successful apply
        // cleared it), so no additional database call is made.
        await _target.StopAsync(CancellationToken.None);

        // Assert
        Assert.Single(_calls);
    }

    // Exceeding the former count-threshold does NOT trigger an early flush.

    [Fact]
    public void Enqueue_DoesNotFlush_EvenWhenCountExceedsFormerThreshold()
    {
        // Arrange - the former early-flush count threshold was 256. Enqueuing well
        // past it must not touch the database: flushing is driven only by the
        // periodic interval and shutdown. No hosted loop is started here, so the
        // only way the database could be reached is an eager count-triggered flush,
        // which must not exist.
        for (var i = 0; i < 300; i++)
        {
            _target.Enqueue($"Package{i}", new NuGetVersion($"1.0.{i}"));
        }

        // Assert - no real-time wait: an eager threshold flush would have fired
        // synchronously during Enqueue above.
        Assert.Empty(_calls);
        _db.Verify(
            d => d.IncrementDownloadsAsync(
                It.IsAny<List<DownloadIncrement>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StopAsync_FlushesPendingIncrements()
    {
        // Arrange
        _target.Enqueue(PackageId, PackageVersion);

        // Act
        await _target.StopAsync(CancellationToken.None);

        // Assert
        Assert.Single(_calls);
        var entry = Assert.Single(_calls[0]);
        Assert.Equal(Key(PackageId, PackageVersion), entry.Key);
        Assert.Equal(1, entry.Delta);
    }

    // A failed flush re-queues the snapshot so the next flush retries it.

    [Fact]
    public async Task StopAsync_RequeuesSnapshot_OnFailure_AndRetriesNextFlush()
    {
        // Arrange - override the default setup: the first flush attempt fails,
        // subsequent attempts succeed, while still recording every snapshot.
        var attempts = 0;
        _db.Setup(d => d.IncrementDownloadsAsync(
                It.IsAny<List<DownloadIncrement>>(),
                It.IsAny<CancellationToken>()))
            .Returns<List<DownloadIncrement>, CancellationToken>(
                (snapshot, _) =>
                {
                    attempts++;
                    _calls.Add(snapshot);
                    return attempts == 1
                        ? Task.FromException(new InvalidOperationException("flush failed"))
                        : Task.CompletedTask;
                });

        _target.Enqueue(PackageId, PackageVersion);

        // Act - first flush: the database throws, so the snapshot is re-queued.
        await _target.StopAsync(CancellationToken.None);
        Assert.Equal(1, attempts);

        // Act - second flush: the re-queued snapshot is retried and succeeds; the
        // buffer is cleared.
        await _target.StopAsync(CancellationToken.None);
        Assert.Equal(2, attempts);

        // Assert - both flushes carried the same coalesced snapshot.
        Assert.Equal(2, _calls.Count);
        foreach (var snapshot in _calls)
        {
            var entry = Assert.Single(snapshot);
            Assert.Equal(Key(PackageId, PackageVersion), entry.Key);
            Assert.Equal(1, entry.Delta);
        }
    }

    // The pending buffer is bounded; a saturated buffer drops overflow.

    [Fact]
    public async Task Enqueue_DropsIncrement_WhenBufferSaturated()
    {
        // Arrange - the pending buffer is capped at MaxPending (50,000 entries).
        // Filling it and enqueuing one more must drop the overflow increment,
        // keeping memory bounded.
        for (var i = 0; i < 50_000; i++)
        {
            _target.Enqueue($"Package{i}", new NuGetVersion($"1.0.{i}"));
        }

        // Act - this 50,001st distinct increment exceeds the cap and must be dropped.
        _target.Enqueue("OverflowPackage", new NuGetVersion("9.9.9"));

        await _target.StopAsync(CancellationToken.None);

        // Assert
        Assert.Single(_calls);
        var snapshot = _calls[0];
        // Exactly the cap, not the cap + 1: the overflow was dropped.
        Assert.Equal(50_000, snapshot.Count);
        Assert.DoesNotContain(snapshot, entry => entry.Key.Id == "OverflowPackage");
    }

    // A cancelled flush must re-queue its snapshot and must not fault shutdown:
    // FlushAsync re-throws OperationCanceledException after re-queuing, and
    // StopAsync's outer catch swallows it (logging a warning) so graceful shutdown
    // is not faulted.

    [Fact]
    public async Task StopAsync_WhenFlushCancelled_RequeuesSnapshot_AndDoesNotThrow()
    {
        // Arrange - override the default setup: every flush attempt throws
        // OperationCanceledException (the production cancellation path), while still
        // recording each attempted snapshot.
        _db.Setup(d => d.IncrementDownloadsAsync(
                It.IsAny<List<DownloadIncrement>>(),
                It.IsAny<CancellationToken>()))
            .Callback<List<DownloadIncrement>, CancellationToken>(
                (snapshot, _) => _calls.Add(snapshot))
            .ThrowsAsync(new OperationCanceledException());

        _target.Enqueue(PackageId, PackageVersion);

        // Act - StopAsync swallows the re-thrown OperationCanceledException from
        // FlushAsync (logged as a warning) so shutdown is not faulted. If StopAsync
        // failed to swallow it, this await would throw.
        await _target.StopAsync(CancellationToken.None);

        // Act again - the cancelled snapshot was re-queued, so a second StopAsync
        // retries it (and fails again the same way).
        await _target.StopAsync(CancellationToken.None);

        // Assert - two attempts were recorded, proving the snapshot survived the
        // cancelled flush and was retried rather than being dropped.
        Assert.Equal(2, _calls.Count);
        foreach (var snapshot in _calls)
        {
            var entry = Assert.Single(snapshot);
            Assert.Equal(Key(PackageId, PackageVersion), entry.Key);
            Assert.Equal(1, entry.Delta);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Enqueue_WithNullOrEmptyId_DoesNotBuffer(string packageId)
    {
        // Arrange - a null or empty id is a no-op guard in Enqueue; it must never
        // reach the buffer.
        _target.Enqueue(packageId, PackageVersion);

        // Act - flush; a buffered entry would surface as a database call here.
        await _target.StopAsync(CancellationToken.None);

        // Assert
        Assert.Empty(_calls);
        _db.Verify(
            d => d.IncrementDownloadsAsync(
                It.IsAny<List<DownloadIncrement>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Enqueue_WithNullVersion_DoesNotBuffer()
    {
        // Arrange - a null version is a no-op guard in Enqueue.
        _target.Enqueue(PackageId, version: null);

        // Act - flush; a buffered entry would surface as a database call here.
        await _target.StopAsync(CancellationToken.None);

        // Assert
        Assert.Empty(_calls);
        _db.Verify(
            d => d.IncrementDownloadsAsync(
                It.IsAny<List<DownloadIncrement>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
