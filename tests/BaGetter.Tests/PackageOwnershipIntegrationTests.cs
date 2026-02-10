using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGetter.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Packaging;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace BaGetter.Tests;

public class PackageOwnershipIntegrationTests : IDisposable
{
    private readonly BaGetterApplication _app;
    private readonly HttpClient _client;

    public PackageOwnershipIntegrationTests(ITestOutputHelper output)
    {
        _app = new BaGetterApplication(output, inMemoryConfiguration: dict =>
        {
            dict.Add("AllowPackageOverwrites", "True");
            dict.Add("PackageDeletionBehavior", "Unlist");
            dict.Add("ApiKey", "master-key");
        });

        _client = _app.CreateClient();
    }

    [Fact]
    public async Task NonOwnerKey_IsRejected_ForOverwriteDeleteAndRelist()
    {
        // Plain English: The first key that uploads a package ID becomes the owner key,
        // and non-owner keys cannot overwrite, delete, or relist it.
        // This exercises the rule: "- non-empty: package ID is owned. Only the matching key (or master) may modify."
        using var package = TestResources.GetResourceStream(TestResources.Package);

        using var firstUpload = await PutPackageAsync(package, "owner-key");
        // Uploading with owner-key succeeds (201 Created).
        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);

        var indexed = await GetIndexedPackageAsync("TestData", "1.2.3");
        // The stored ownership key for TestData 1.2.3 is owner-key.
        Assert.Equal("owner-key", indexed.ApiKey);

        package.Position = 0;
        using var overwriteAttempt = await PutPackageAsync(package, "other-key");
        // Non-owner key cannot overwrite (401 Unauthorized).
        Assert.Equal(HttpStatusCode.Unauthorized, overwriteAttempt.StatusCode);

        using var deleteAttempt = await DeletePackageAsync("TestData", "1.2.3", "other-key");
        // Non-owner key cannot delete/unlist (401 Unauthorized).
        Assert.Equal(HttpStatusCode.Unauthorized, deleteAttempt.StatusCode);

        using var ownerDelete = await DeletePackageAsync("TestData", "1.2.3", "owner-key");
        // Owner key can delete/unlist (204 No Content).
        Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);

        using var relistAttempt = await RelistPackageAsync("TestData", "1.2.3", "other-key");
        // Non-owner key cannot relist (401 Unauthorized).
        Assert.Equal(HttpStatusCode.Unauthorized, relistAttempt.StatusCode);

        using var ownerRelist = await RelistPackageAsync("TestData", "1.2.3", "owner-key");
        // Owner key can relist (200 OK).
        Assert.Equal(HttpStatusCode.OK, ownerRelist.StatusCode);
    }

    [Fact]
    public async Task MasterKey_OverridesOwnership_ForDeleteAndRelist()
    {
        // Plain English: The master key can always delete/relist a package, even if it is owned by another key.
        // This exercises the rule: "- Master key (BaGetterOptions.ApiKey) always authorizes."
        using var package = TestResources.GetResourceStream(TestResources.Package);
        using var firstUpload = await PutPackageAsync(package, "owner-key");
        // Initial upload succeeds (201 Created).
        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);

        using var masterDelete = await DeletePackageAsync("TestData", "1.2.3", "master-key");
        // Master key can delete/unlist (204 No Content).
        Assert.Equal(HttpStatusCode.NoContent, masterDelete.StatusCode);

        using var masterRelist = await RelistPackageAsync("TestData", "1.2.3", "master-key");
        // Master key can relist (200 OK).
        Assert.Equal(HttpStatusCode.OK, masterRelist.StatusCode);
    }

    [Fact]
    public async Task OwnerKey_CanOverwritePackage()
    {
        // Plain English: The owner key can overwrite the package (same ID+version) when overwrites are enabled.
        using var package = TestResources.GetResourceStream(TestResources.Package);

        using var firstUpload = await PutPackageAsync(package, "owner-key");
        // Initial upload succeeds (201 Created).
        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);

        package.Position = 0;
        using var overwriteWithOwner = await PutPackageAsync(package, "owner-key");
        // Owner key can overwrite when overwrites are enabled (201 Created).
        Assert.Equal(HttpStatusCode.Created, overwriteWithOwner.StatusCode);
    }

    [Fact]
    public async Task NonOwnerKey_IsRejected_ForNewVersionUpload()
    {
        // Plain English: Ownership is per *package ID*, not per version.
        // If version 1.2.3 was created with owner-key, then uploading 1.2.4 with other-key is rejected.
        using var packageV123 = TestResources.GetResourceStream(TestResources.Package);

        using var firstUpload = await PutPackageAsync(packageV123, "owner-key");
        // Uploading 1.2.3 with owner-key succeeds (201 Created).
        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);

        using var packageV124 = CreateTestPackageStream("TestData", "1.2.4");

        using var wrongKeyUpload = await PutPackageAsync(packageV124, "other-key");
        // Non-owner key cannot upload a new version (401 Unauthorized).
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKeyUpload.StatusCode);

        packageV124.Position = 0;
        using var ownerKeyUpload = await PutPackageAsync(packageV124, "owner-key");
        // Owner key can upload a new version (201 Created).
        Assert.Equal(HttpStatusCode.Created, ownerKeyUpload.StatusCode);

        var indexed = await GetIndexedPackageAsync("TestData", "1.2.4");
        // Ownership key stays owner-key on the new version.
        Assert.Equal("owner-key", indexed.ApiKey);
    }

    [Fact]
    public async Task PublishWithMasterKey_SetsOwnershipToMasterKey()
    {
        // Plain English: If the master key is used to publish a brand new package ID,
        // the stored ownership key becomes the master key.
        // This also verifies that non-master keys cannot modify a master-owned package.
        using var package = CreateTestPackageStream("MasterOwned", "1.0.0");

        using var firstUpload = await PutPackageAsync(package, "master-key");
        // Publishing with the master key succeeds (201 Created).
        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);

        var indexed = await GetIndexedPackageAsync("MasterOwned", "1.0.0");
        // The stored ownership key is master-key.
        Assert.Equal("master-key", indexed.ApiKey);

        using var overwriteAttempt = await PutPackageAsync(CreateTestPackageStream("MasterOwned", "1.0.0"), "other-key");
        // Non-owner key cannot overwrite a master-owned package (401 Unauthorized).
        Assert.Equal(HttpStatusCode.Unauthorized, overwriteAttempt.StatusCode);
    }

    [Fact]
    public async Task MasterKeyCanOverwriteOwnedPackage_ButOwnershipKeyDoesNotChange()
    {
        // Plain English: Master can overwrite, but it should not steal ownership.
        // When the package ID is already owned, uploads authenticated via the master key must keep the existing owner key.
        using var package = TestResources.GetResourceStream(TestResources.Package);

        using var firstUpload = await PutPackageAsync(package, "owner-key");
        // Initial upload succeeds (201 Created).
        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);
        // Ownership key is owner-key.
        Assert.Equal("owner-key", (await GetIndexedPackageAsync("TestData", "1.2.3")).ApiKey);

        using var overwriteWithMaster = await PutPackageAsync(CreateTestPackageStream("TestData", "1.2.3"), "master-key");
        // Master key can overwrite (201 Created).
        Assert.Equal(HttpStatusCode.Created, overwriteWithMaster.StatusCode);
        // Overwrite does not change ownership key.
        Assert.Equal("owner-key", (await GetIndexedPackageAsync("TestData", "1.2.3")).ApiKey);
    }

    [Fact]
    public async Task UnownedPackage_OnlyMasterMayModify_AndMasterClaimsOwnershipOnUpload()
    {
        // Plain English: If a package ID exists but has an empty ownership key (legacy/unowned),
        // only the master key may modify it, and a master upload should claim ownership.
        // This exercises the rule: "- \"\"  : package ID exists but has no owner (legacy/unowned/mirrored). Only master may modify/claim."
        using var package = TestResources.GetResourceStream(TestResources.Package);

        using var firstUpload = await PutPackageAsync(package, "owner-key");
        // Initial upload succeeds (201 Created).
        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);

        await SetOwnershipKeyForAllVersionsAsync("TestData", string.Empty);

        using var overwriteAttempt = await PutPackageAsync(CreateTestPackageStream("TestData", "1.2.3"), "other-key");
        // Unowned package cannot be modified by non-master (401 Unauthorized).
        Assert.Equal(HttpStatusCode.Unauthorized, overwriteAttempt.StatusCode);

        using var masterOverwrite = await PutPackageAsync(CreateTestPackageStream("TestData", "1.2.3"), "master-key");
        // Master key can modify/claim unowned package (201 Created).
        Assert.Equal(HttpStatusCode.Created, masterOverwrite.StatusCode);

        // After a master upload, ownership should be claimed as master-key.
        // Ownership key becomes master-key.
        Assert.Equal("master-key", (await GetIndexedPackageAsync("TestData", "1.2.3")).ApiKey);

        using var secondAttempt = await PutPackageAsync(CreateTestPackageStream("TestData", "1.2.3"), "other-key");
        // Non-owner key still cannot modify after claim (401 Unauthorized).
        Assert.Equal(HttpStatusCode.Unauthorized, secondAttempt.StatusCode);
    }

    private Task<HttpResponseMessage> PutPackageAsync(Stream package, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "api/v2/package")
        {
            Content = new StreamContent(package)
        };
        request.Headers.Add("X-NuGet-ApiKey", apiKey);
        return _client.SendAsync(request);
    }

    private async Task<Package> GetIndexedPackageAsync(string id, string normalizedVersion)
    {
        var scopeFactory = _app.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.Packages.SingleAsync(
            p => p.Id == id && p.NormalizedVersionString == normalizedVersion,
            default);
    }

    private async Task SetOwnershipKeyForAllVersionsAsync(string id, string apiKey)
    {
        var scopeFactory = _app.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        var packages = await context.Packages.Where(p => p.Id == id).ToListAsync(default);
        foreach (var package in packages)
        {
            package.ApiKey = apiKey;
        }

        await context.SaveChangesAsync(default);
    }

    private static MemoryStream CreateTestPackageStream(string id, string version)
    {
        var builder = new PackageBuilder
        {
            Id = id,
            Version = NuGetVersion.Parse(version),
            Description = "Test description",
        };
        builder.Authors.Add("Test author");

        var assemblyFile = typeof(PackageOwnershipIntegrationTests).Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });

        var stream = new MemoryStream();
        builder.Save(stream);
        stream.Position = 0;
        return stream;
    }

    private Task<HttpResponseMessage> DeletePackageAsync(string id, string version, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"api/v2/package/{id}/{version}");
        request.Headers.Add("X-NuGet-ApiKey", apiKey);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> RelistPackageAsync(string id, string version, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"api/v2/package/{id}/{version}");
        request.Headers.Add("X-NuGet-ApiKey", apiKey);
        return _client.SendAsync(request);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
