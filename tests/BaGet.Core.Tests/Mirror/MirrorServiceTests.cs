using System.Threading.Tasks;
using BaGet.Core.Indexing;
using BaGet.Core.Metadata;
using BaGet.Core.Mirror;
using BaGet.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.Mirror
{
    public class MirrorServiceTests
    {
        public class FindPackageVersionsOrNullAsync : FactsBase
        {
            [Fact]
            public async Task MergesLocalAndUpstream()
            {
            }
        }

        public class FindPackagesOrNullAsync : FactsBase
        {
            [Fact]
            public async Task MergesLocalAndUpstream()
            {
            }
        }

        public class MirrorAsync : FactsBase
        {
            [Fact]
            public async Task SkipsIfAlreadyMirrored()
            {
            }

            [Fact]
            public async Task SkipsIfUpstreamDoesntHavePackage()
            {
            }

            [Fact]
            public async Task MirrorsPackage()
            {
            }
        }

        public class FactsBase
        {
            private readonly Mock<IPackageService> _packages;
            private readonly Mock<IPackageContentService> _content;
            private readonly Mock<IPackageMetadataService> _metadata;
            private readonly Mock<IPackageIndexingService> _indexer;

            private readonly MirrorService _target;

            public FactsBase()
            {
                _packages = new Mock<IPackageService>();
                _content = new Mock<IPackageContentService>();
                _metadata = new Mock<IPackageMetadataService>();
                _indexer = new Mock<IPackageIndexingService>();

                _target = new MirrorService(
                    _packages.Object,
                    _content.Object,
                    _metadata.Object,
                    _indexer.Object,
                    Mock.Of<ILogger<MirrorService>>());
            }
        }
    }
}
