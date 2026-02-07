using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BaGetter.Core;

public class ApiKeyAuthenticationService : IAuthenticationService
{
    private readonly string _apiKey;
    private readonly ApiKey[] _apiKeys;

    public ApiKeyAuthenticationService(IOptionsSnapshot<BaGetterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _apiKey = string.IsNullOrEmpty(options.Value.ApiKey) ? null : options.Value.ApiKey;
        _apiKeys = options.Value.Authentication?.ApiKeys ?? [];
    }

    public Task<bool> AuthenticateAsync(string apiKey, CancellationToken cancellationToken)
        => Task.FromResult(Authenticate(apiKey, packageKey: null));

    public Task<bool> AuthenticateAsync(string apiKey, string packageKey, CancellationToken cancellationToken)
        => Task.FromResult(Authenticate(apiKey, packageKey));

    private bool Authenticate(string apiKey, string packageKey)
    {
        // Authentication rules (Reloaded-II fork):
        // - Disabled auth: if there is no configured master key AND no configured API key allowlist.
        // - Enabled auth:
        //   - Request must provide a non-empty API key.
        //   - Master key (BaGetterOptions.ApiKey) always authorizes.
        //   - If an allowlist (Authentication:ApiKeys) is configured (non-empty), the request key must be in it.
        //   - If the allowlist is empty, ANY non-empty key is treated as a valid "user" key.
        // - packageKey represents ownership state for a package ID:
        //   - null: package ID does not exist yet (new package). Any valid user key can create it and becomes owner.
        //   - ""  : package ID exists but has no owner (legacy/unowned/mirrored). Only master may modify/claim.
        //   - non-empty: package ID is owned. Only the matching key (or master) may modify.

        // No authentication is necessary if there is no required API key.
        if (_apiKey == null && (_apiKeys.Length == 0)) return true;

        if (string.IsNullOrEmpty(apiKey)) return false;

        var isMaster = _apiKey != null && string.Equals(_apiKey, apiKey, StringComparison.Ordinal);
        if (isMaster) return true;

        // If an allowlist is configured, enforce it. If none is configured, accept any non-empty key.
        if (_apiKeys.Length > 0 && !_apiKeys.Any(x => string.Equals(x.Key, apiKey, StringComparison.Ordinal)))
        {
            return false;
        }

        // New package ID: any valid user key can proceed.
        if (packageKey == null) return true;

        // Existing but unowned package ID: only master may proceed (checked above).
        if (packageKey.Length == 0) return false;

        // Existing owned package ID.
        return string.Equals(packageKey, apiKey, StringComparison.Ordinal);
    }
}
