using Azure.Core.Serialization;
using Microsoft.RetireaBot.Contracts;
using Microsoft.RetireaBot.Domain;

namespace Microsoft.RetireaBot.Tests.Contracts
{
    public class ResponseProjectorRegistryTests
    {
        public sealed class FakeProjector(ApiVersion v) : IResponseProjector
        {
            public ApiVersion Version => v;
            public Task<ProjectedResponse> Project(RetirementReport report, string mediaType) => Task.FromResult(new ProjectedResponse("application/json", BinaryData.FromString(string.Empty)));
        }

        [Fact]
        public void SupportedVersions_VersionsAddedShouldBeInList()
        {
            var reg = new ResponseProjectorRegistry(new[]
            {
                new FakeProjector(new ApiVersion(new DateOnly(2026, 6, 25), false)),
                new FakeProjector(new ApiVersion(new DateOnly(2026, 12, 1), false)),
            });

            Assert.Equal([new ApiVersion(new DateOnly(2026, 12, 1), false), new ApiVersion(new DateOnly(2026, 6, 25), false)], reg.SupportedVersions);
        }

        [Fact]
        public void TryResolve_PinnedNewerDate_ReturnsMostRecentPrior()
        {
            var reg = new ResponseProjectorRegistry(new[]
            {
                new FakeProjector(new ApiVersion(new DateOnly(2026, 6, 25), false)),
                new FakeProjector(new ApiVersion(new DateOnly(2026, 12, 1), false)),
            });

            Assert.True(reg.TryResolve(new ApiVersion(new DateOnly(2027, 1, 1), false), out var p));
            Assert.Equal(new DateOnly(2026, 12, 1), p.Version.Date);
        }

        [Fact]
        public void TryResolve_PreviewAndGaAreIsolated()
        {
            var reg = new ResponseProjectorRegistry(new[]
            {
                  new FakeProjector(new ApiVersion(new DateOnly(2026, 6, 25), true)),
            });

            Assert.False(reg.TryResolve(new ApiVersion(new DateOnly(2026, 6, 25), false), out _));
            Assert.True(reg.TryResolve(new ApiVersion(new DateOnly(2026, 6, 25), true), out _));
        }

        [Fact]
        public void TryResolve_OlderThanEarliest_ReturnsFalse()
        {
            var reg = new ResponseProjectorRegistry(new[]
            {
                  new FakeProjector(new ApiVersion(new DateOnly(2026, 6, 25), false)),
            });

            Assert.False(reg.TryResolve(new ApiVersion(new DateOnly(2020, 1, 1), false), out _));
        }
    }
}