using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// Used to create URLs to different resources in the NuGet protocol.
    /// </summary>
    public interface IUrlGenerator
    {
        string GetPackageContentResourceUrl();
        string GetPackageMetadataResourceUrl();
        string GetPackagePublishResourceUrl();
        string GetSymbolPublishResourceUrl();
        string GetSearchResourceUrl();
        string GetAutocompleteResourceUrl();

        string GetRegistrationIndexUrl(string id);
        string GetRegistrationPageUrl(string id, NuGetVersion lower, NuGetVersion upper);
        string GetRegistrationLeafUrl(string id, NuGetVersion version);

        string GetPackageVersionsUrl(string id);
        string GetPackageDownloadUrl(string id, NuGetVersion version);
        string GetPackageManifestDownloadUrl(string id, NuGetVersion version);
    }

    public interface IUrlGeneratorFactory
    {
        Task<IUrlGenerator> CreateAsync();
    }
}
