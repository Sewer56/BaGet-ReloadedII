using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuGet.Versioning;

namespace BaGetter.Web;

public class PackagePublishController : Controller
{
    private readonly IAuthenticationService _authentication;
    private readonly IPackageIndexingService _indexer;
    private readonly IPackageDatabase _packages;
    private readonly IPackageDeletionService _deleteService;
    private readonly IOptionsSnapshot<BaGetterOptions> _options;
    private readonly ILogger<PackagePublishController> _logger;

    public PackagePublishController(
        IAuthenticationService authentication,
        IPackageIndexingService indexer,
        IPackageDatabase packages,
        IPackageDeletionService deletionService,
        IOptionsSnapshot<BaGetterOptions> options,
        ILogger<PackagePublishController> logger)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _deleteService = deletionService ?? throw new ArgumentNullException(nameof(deletionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
    public async Task Upload(CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode)
        {
            HttpContext.Response.StatusCode = 401;
            return;
        }

        using var uploadStream = await Request.GetUploadStreamOrNullAsync(cancellationToken);
        if (uploadStream == null)
        {
            HttpContext.Response.StatusCode = 400;
            return;
        }

        var apiKey = Request.GetApiKey();
        string packageId;
        try
        {
            using var packageReader = new PackageArchiveReader(uploadStream, leaveStreamOpen: true);
            var metadata = packageReader.GetPackageMetadata();
            packageId = metadata.Id;
            uploadStream.Position = 0;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to parse package metadata from upload stream");
            HttpContext.Response.StatusCode = 400;
            return;
        }

        var existingPackages = await _packages.FindAsync(packageId, includeUnlisted: true, cancellationToken);
        var existingOwnershipKey = GetOwnershipKeyOrNull(packageId, existingPackages);

        if (!await _authentication.AuthenticateAsync(apiKey, existingOwnershipKey, cancellationToken))
        {
            HttpContext.Response.StatusCode = 401;
            return;
        }

        var ownershipKey = existingOwnershipKey ?? apiKey;

        // Claim legacy/unowned packages with the master key.
        // AuthenticateAsync(apiKey, "", ...) only succeeds for the master key, but we explicitly
        // check the configured master key here to avoid changing ownership when auth is disabled.
        if (existingOwnershipKey != null && existingOwnershipKey.Length == 0 &&
            !string.IsNullOrEmpty(_options.Value.ApiKey) &&
            string.Equals(_options.Value.ApiKey, apiKey, StringComparison.Ordinal))
        {
            ownershipKey = apiKey;
        }

        try
        {
            var result = await _indexer.IndexAsync(uploadStream, ownershipKey, cancellationToken);

            switch (result)
            {
                case PackageIndexingResult.InvalidPackage:
                    HttpContext.Response.StatusCode = 400;
                    break;

                case PackageIndexingResult.PackageAlreadyExists:
                    HttpContext.Response.StatusCode = 409;
                    break;

                case PackageIndexingResult.Success:
                    HttpContext.Response.StatusCode = 201;
                    break;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during package upload");

            HttpContext.Response.StatusCode = 500;
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(string id, string version, CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode)
        {
            return Unauthorized();
        }

        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        // Ensure the specific version exists.
        var package = await _packages.FindOrNullAsync(id, nugetVersion, includeUnlisted: true, cancellationToken);
        if (package == null) return NotFound();

        // Enforce ownership at the package ID level (all versions share the same ownership key).
        var existingPackages = await _packages.FindAsync(id, includeUnlisted: true, cancellationToken);
        var ownershipKey = GetOwnershipKeyOrNull(id, existingPackages);

        if (!await _authentication.AuthenticateAsync(Request.GetApiKey(), ownershipKey, cancellationToken))
        {
            return Unauthorized();
        }

        if (await _deleteService.TryDeletePackageAsync(id, nugetVersion, cancellationToken))
        {
            return NoContent();
        }
        else
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<IActionResult> Relist(string id, string version, CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode)
        {
            return Unauthorized();
        }

        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        // Ensure the specific version exists.
        var package = await _packages.FindOrNullAsync(id, nugetVersion, includeUnlisted: true, cancellationToken);
        if (package == null) return NotFound();

        // Enforce ownership at the package ID level (all versions share the same ownership key).
        var existingPackages = await _packages.FindAsync(id, includeUnlisted: true, cancellationToken);
        var ownershipKey = GetOwnershipKeyOrNull(id, existingPackages);

        if (!await _authentication.AuthenticateAsync(Request.GetApiKey(), ownershipKey, cancellationToken))
        {
            return Unauthorized();
        }

        if (await _packages.RelistPackageAsync(id, nugetVersion, cancellationToken))
        {
            return Ok();
        }
        else
        {
            return NotFound();
        }
    }

    private string GetOwnershipKeyOrNull(string packageId, System.Collections.Generic.IReadOnlyList<Package> existingPackages)
    {
        if (existingPackages == null || existingPackages.Count == 0) return null;

        var distinctNonEmptyKeys = existingPackages
            .Select(p => p.ApiKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        if (distinctNonEmptyKeys.Length == 0)
        {
            // Existing package ID but with no ownership key.
            return string.Empty;
        }

        if (distinctNonEmptyKeys.Length > 1)
        {
            // This should not happen in a healthy per-package-key system. Require master key.
            _logger.LogWarning(
                "Package {PackageId} has multiple ownership keys in the database; requiring master key to modify.",
                packageId);
            return string.Empty;
        }

        return distinctNonEmptyKeys[0];
    }
}
