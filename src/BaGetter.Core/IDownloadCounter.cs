using NuGet.Versioning;

namespace BaGetter.Core;

/// <summary>
/// Accepts package-download increments off the request hot path and persists
/// them in batches from a background service.
/// </summary>
public interface IDownloadCounter
{
    /// <summary>
    /// Record that a package version was downloaded. Returns immediately; the
    /// increment is persisted asynchronously, in batches, by a background service.
    /// </summary>
    /// <param name="packageId">The id of the package that was downloaded.</param>
    /// <param name="version">The version of the package that was downloaded.</param>
    void Enqueue(string packageId, NuGetVersion version);
}
