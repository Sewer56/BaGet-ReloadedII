using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BaGetter.Core.Tests.Authentication;

public class ApiKeyAuthenticationServiceTests
{
    // These tests intentionally mirror the documented rules in
    // `src/BaGetter.Core/Authentication/ApiKeyAuthenticationService.cs`.

    [Fact]
    public async Task AuthenticateAsync_WhenPackageIsOwned_AndKeyMatchesOwner_ReturnsTrue()
    {
        // Covers: "- non-empty: package ID is owned. Only the matching key (or master) may modify."
        var target = CreateTarget(masterKey: "master-key");
        var result = await target.AuthenticateAsync("owner-key", "owner-key", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenPackageIsOwned_AndKeyDoesNotMatchOwner_ReturnsFalse()
    {
        // Covers: "- non-empty: package ID is owned. Only the matching key (or master) may modify."
        var target = CreateTarget(masterKey: "master-key");
        var result = await target.AuthenticateAsync("other-key", "owner-key", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task AuthenticateAsync_MasterKeyAlwaysAuthorizes_ForOwnedPackage()
    {
        // Covers: "- Master key (BaGetterOptions.ApiKey) always authorizes."
        var target = CreateTarget(masterKey: "master-key");
        var result = await target.AuthenticateAsync("master-key", "owner-key", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenAllowlistIsEmpty_AllowsAnyNonEmptyKey_ForNewPackage()
    {
        // Covers: "- If the allowlist is empty, ANY non-empty key is treated as a valid \"user\" key."
        // Covers: "- null: package ID does not exist yet (new package). Any valid user key can create it and becomes owner."
        var target = CreateTarget(masterKey: "master-key");
        var result = await target.AuthenticateAsync("some-random-key", packageKey: null, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenAllowlistIsExplicitlyConfiguredButEmpty_AllowsAnyNonEmptyKey_ForNewPackage()
    {
        // Covers: "- If the allowlist is empty, ANY non-empty key is treated as a valid \"user\" key."
        // This simulates an appsettings.json that has an "Authentication" object but no ApiKeys entries.
        var target = CreateTarget(masterKey: "master-key", allowedKeys: Array.Empty<string>());
        var result = await target.AuthenticateAsync("some-random-key", packageKey: null, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenPackageIsUnowned_OnlyMasterMayModifyOrClaim()
    {
        // Covers: "- \"\"  : package ID exists but has no owner (legacy/unowned/mirrored). Only master may modify/claim."
        var target = CreateTarget(masterKey: "master-key");

        var nonMasterResult = await target.AuthenticateAsync("some-random-key", packageKey: string.Empty, CancellationToken.None);
        Assert.False(nonMasterResult);

        var masterResult = await target.AuthenticateAsync("master-key", packageKey: string.Empty, CancellationToken.None);
        Assert.True(masterResult);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenAuthEnabled_EmptyApiKey_ReturnsFalse()
    {
        // Covers: "- Enabled auth: - Request must provide a non-empty API key."
        var target = CreateTarget(masterKey: "master-key");
        var result = await target.AuthenticateAsync("", "owner-key", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenAuthEnabled_NullApiKey_ReturnsFalse()
    {
        // Covers: "- Enabled auth: - Request must provide a non-empty API key."
        var target = CreateTarget(masterKey: "master-key");
        var result = await target.AuthenticateAsync(null, "owner-key", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenAuthDisabled_AllowsAnyApiKey_IncludingNullOrEmpty()
    {
        // Covers: "- Disabled auth: if there is no configured master key AND no configured API key allowlist."
        var target = CreateTarget(masterKey: null);
        Assert.True(await target.AuthenticateAsync(null, packageKey: null, CancellationToken.None));
        Assert.True(await target.AuthenticateAsync(string.Empty, packageKey: string.Empty, CancellationToken.None));
        Assert.True(await target.AuthenticateAsync("any-key", packageKey: "owned-key", CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticateAsync_WhenMasterKeyConfiguredAsEmptyString_TreatsItAsUnconfigured()
    {
        // Covers: "- Disabled auth: if there is no configured master key AND no configured API key allowlist."
        // ApiKeyAuthenticationService treats an empty master key as "not configured".
        var target = CreateTarget(masterKey: string.Empty);
        Assert.True(await target.AuthenticateAsync(null, packageKey: null, CancellationToken.None));
        Assert.True(await target.AuthenticateAsync(string.Empty, packageKey: string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticateAsync_WhenAllowlistConfigured_RejectsKeysNotInAllowlist()
    {
        // Covers: "- If an allowlist (Authentication:ApiKeys) is configured (non-empty), the request key must be in it."
        var target = CreateTarget(masterKey: "master-key", allowedKeys: ["allowed-key"]);
        var result = await target.AuthenticateAsync("unknown-key", packageKey: null, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenAllowlistConfigured_AllowsKeyInAllowlist_ForNewPackage()
    {
        // Covers: "- If an allowlist (Authentication:ApiKeys) is configured (non-empty), the request key must be in it."
        // Covers: "- null: package ID does not exist yet (new package). Any valid user key can create it and becomes owner."
        var target = CreateTarget(masterKey: "master-key", allowedKeys: ["allowed-key"]);
        var result = await target.AuthenticateAsync("allowed-key", packageKey: null, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenAllowlistConfigured_RejectsKeyNotInAllowlist_EvenIfItMatchesPackageOwner()
    {
        // Covers: "- If an allowlist (Authentication:ApiKeys) is configured (non-empty), the request key must be in it."
        // This guards against a config change accidentally re-enabling a removed key.
        var target = CreateTarget(masterKey: "master-key", allowedKeys: ["allowed-key"]);
        var result = await target.AuthenticateAsync("owner-key", packageKey: "owner-key", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task AuthenticateAsync_MasterKeyAlwaysAuthorizes_EvenWhenAllowlistConfigured()
    {
        // Covers: "- Master key (BaGetterOptions.ApiKey) always authorizes."
        var target = CreateTarget(masterKey: "master-key", allowedKeys: ["allowed-key"]);
        var result = await target.AuthenticateAsync("master-key", packageKey: "owner-key", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenNoMasterKeyButAllowlistConfigured_UnownedPackageCannotBeModified()
    {
        // Covers: "- \"\"  : package ID exists but has no owner (legacy/unowned/mirrored). Only master may modify/claim."
        var target = CreateTarget(masterKey: null, allowedKeys: ["allowed-key"]);
        var result = await target.AuthenticateAsync("allowed-key", packageKey: string.Empty, CancellationToken.None);
        Assert.False(result);
    }

    private static ApiKeyAuthenticationService CreateTarget(string masterKey, string[] allowedKeys = null)
    {
        var options = new Mock<IOptionsSnapshot<BaGetterOptions>>();
        options.Setup(x => x.Value).Returns(new BaGetterOptions
        {
            ApiKey = masterKey,
            Authentication = allowedKeys == null
                ? null
                : new NugetAuthenticationOptions
                {
                    ApiKeys = allowedKeys.Select(k => new ApiKey { Key = k }).ToArray()
                }
        });

        return new ApiKeyAuthenticationService(options.Object);
    }
}
