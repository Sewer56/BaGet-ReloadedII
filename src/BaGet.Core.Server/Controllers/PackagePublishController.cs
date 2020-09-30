using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Authentication;
using BaGet.Core.Configuration;
using BaGet.Core.Indexing;
using BaGet.Core.State;
using BaGet.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace BaGet.Controllers
{
    public class PackagePublishController : Controller
    {
        private readonly IAuthenticationService _authentication;
        private readonly IPackageIndexingService _indexer;
        private readonly IPackageService _packages;
        private readonly IPackageDeletionService _deleteService;
        private readonly IOptionsSnapshot<BaGetOptions> _options;
        private readonly ILogger<PackagePublishController> _logger;

        public PackagePublishController(
            IAuthenticationService authentication,
            IPackageIndexingService indexer,
            IPackageService packages,
            IPackageDeletionService deletionService,
            IOptionsSnapshot<BaGetOptions> options,
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
            if (_options.Value.IsReadOnlyMode || !_authentication.Authenticate(Request.GetApiKey(), null))
            {
                HttpContext.Response.StatusCode = 401;
                return;
            }

            try
            {
                using (var uploadStream = await Request.GetUploadStreamOrNullAsync(cancellationToken))
                {
                    if (uploadStream == null)
                    {
                        HttpContext.Response.StatusCode = 400;
                        return;
                    }

                    var result = await _indexer.IndexAsync(uploadStream, cancellationToken, Request.GetApiKey());

                    switch (result)
                    {
                        case PackageIndexingResult.BadApiKey:
                            HttpContext.Response.StatusCode = 401;
                            break;

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


            // Check API Key
            var apiKey = (await _packages.FindOrNullAsync(id, NuGetVersion.Parse(version)))?.ApiKey;
            if (!await _authentication.AuthenticateAsync(Request.GetApiKey(), apiKey))
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
        public async Task<IActionResult> Relist(string id, string version)
        {
            if (_options.Value.IsReadOnlyMode)
            {
                return Unauthorized();
            }

            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            // Check API Key
            var apiKey = (await _packages.FindOrNullAsync(id, NuGetVersion.Parse(version)))?.ApiKey;
            if (!await _authentication.AuthenticateAsync(Request.GetApiKey(), apiKey))
            {
                return Unauthorized();
            }

            if (await _packages.RelistPackageAsync(id, nugetVersion))
            {
                return Ok();
            }
            else
            {
                return NotFound();
            }
        }
    }
}
