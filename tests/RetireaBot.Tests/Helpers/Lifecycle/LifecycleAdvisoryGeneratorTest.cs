using Microsoft.RetireaBot.Helpers.Lifecycle;
using Microsoft.RetireaBot.Models.Lifecycle;

namespace Microsoft.RetireaBot.Tests.Helpers.Lifecycle
{
    public class LifecycleAdvisoryGeneratorTest
    {
        private static VersionedResource AksResource(string version) => new()
        {
            Id = $"/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.ContainerService/managedClusters/aks-{version}",
            Name = $"aks-{version}",
            Type = "microsoft.containerservice/managedclusters",
            SubscriptionId = "sub-1",
            ResourceGroup = "rg-1",
            Location = "eastus",
            Version = version,
        };

        private static VersionedResource PgResource(string version) => new()
        {
            Id = $"/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.DBforPostgreSQL/flexibleServers/pg-{version}",
            Name = $"pg-{version}",
            Type = "microsoft.dbforpostgresql/flexibleservers",
            SubscriptionId = "sub-1",
            ResourceGroup = "rg-1",
            Location = "eastus",
            Version = version,
        };

        [Fact]
        public void TryCreate_AksWithinWarningWindow_ReturnsAdvisory()
        {
            var now = new DateTime(2026, 1, 1);
            var entries = new Dictionary<string, LifecycleEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["1.31"] = new LifecycleEntry { Product = LifecycleProduct.AksKubernetes, Version = "1.31", EndOfLife = new DateTime(2026, 3, 31) },
            };

            var advisory = LifecycleAdvisoryGenerator.TryCreate(AksResource("1.31.5"), entries, LifecycleProduct.AksKubernetes, now, TimeSpan.FromDays(180));

            Assert.NotNull(advisory);
            Assert.Equal("Medium", advisory!.Properties.Impact);
            Assert.Equal("2026-03-31", advisory.Properties.ExtendedProperties.RetirementDate);
            Assert.Equal("AKS Kubernetes 1.31", advisory.Properties.ExtendedProperties.RetirementFeatureName);
            Assert.Contains("AKS Kubernetes 1.31", advisory.Properties.ShortDescription.Problem);
            Assert.Equal("ServiceUpgradeAndRetirement", advisory.Properties.ExtendedProperties.RecommendationSubCategory);
        }

        [Fact]
        public void TryCreate_AksOutsideWindow_ReturnsNull()
        {
            var now = new DateTime(2025, 1, 1);
            var entries = new Dictionary<string, LifecycleEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["1.35"] = new LifecycleEntry { Product = LifecycleProduct.AksKubernetes, Version = "1.35", EndOfLife = new DateTime(2027, 3, 31) },
            };

            var advisory = LifecycleAdvisoryGenerator.TryCreate(AksResource("1.35.0"), entries, LifecycleProduct.AksKubernetes, now, TimeSpan.FromDays(180));
            Assert.Null(advisory);
        }

        [Fact]
        public void TryCreate_AksPastEol_ReturnsHighImpactAdvisory()
        {
            var now = new DateTime(2026, 5, 1);
            var entries = new Dictionary<string, LifecycleEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["1.31"] = new LifecycleEntry { Product = LifecycleProduct.AksKubernetes, Version = "1.31", EndOfLife = new DateTime(2025, 11, 1) },
            };

            var advisory = LifecycleAdvisoryGenerator.TryCreate(AksResource("1.31.0"), entries, LifecycleProduct.AksKubernetes, now, TimeSpan.FromDays(180));

            Assert.NotNull(advisory);
            Assert.Equal("High", advisory!.Properties.Impact);
            Assert.Contains("reached end of life", advisory.Properties.ShortDescription.Problem);
        }

        [Fact]
        public void TryCreate_UnknownVersion_ReturnsNull()
        {
            var entries = new Dictionary<string, LifecycleEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["1.31"] = new LifecycleEntry { Product = LifecycleProduct.AksKubernetes, Version = "1.31", EndOfLife = new DateTime(2026, 3, 31) },
            };

            var advisory = LifecycleAdvisoryGenerator.TryCreate(AksResource("1.99.0"), entries, LifecycleProduct.AksKubernetes, DateTime.UtcNow, TimeSpan.FromDays(180));
            Assert.Null(advisory);
        }

        [Fact]
        public void TryCreate_PostgreSqlMatchesByMajorVersion()
        {
            var now = new DateTime(2026, 5, 1);
            var entries = new Dictionary<string, LifecycleEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["13"] = new LifecycleEntry { Product = LifecycleProduct.AzurePostgreSQLFlexible, Version = "13", EndOfLife = new DateTime(2026, 7, 31) },
            };

            var advisory = LifecycleAdvisoryGenerator.TryCreate(PgResource("13"), entries, LifecycleProduct.AzurePostgreSQLFlexible, now, TimeSpan.FromDays(180));

            Assert.NotNull(advisory);
            Assert.Contains("PostgreSQL flexible server 13", advisory!.Properties.ExtendedProperties.RetirementFeatureName);
            Assert.Equal("microsoft.dbforpostgresql/flexibleservers", advisory.Properties.ImpactedField);
        }

        [Fact]
        public void TryCreate_DeterministicName_ForSameResourceAndType()
        {
            var entries = new Dictionary<string, LifecycleEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["1.31"] = new LifecycleEntry { Product = LifecycleProduct.AksKubernetes, Version = "1.31", EndOfLife = new DateTime(2026, 3, 31) },
            };

            var a = LifecycleAdvisoryGenerator.TryCreate(AksResource("1.31.0"), entries, LifecycleProduct.AksKubernetes, new DateTime(2026, 1, 1), TimeSpan.FromDays(180));
            var b = LifecycleAdvisoryGenerator.TryCreate(AksResource("1.31.0"), entries, LifecycleProduct.AksKubernetes, new DateTime(2026, 2, 1), TimeSpan.FromDays(180));

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(a!.Name, b!.Name);
        }

        [Theory]
        [InlineData(LifecycleProduct.AksKubernetes, "1.31.0", "1.31")]
        [InlineData(LifecycleProduct.AksKubernetes, "v1.32", "1.32")]
        [InlineData(LifecycleProduct.AzurePostgreSQLFlexible, "13", "13")]
        [InlineData(LifecycleProduct.AzurePostgreSQLFlexible, "13.4", "13")]
        public void NormaliseVersion_FollowsProductRules(LifecycleProduct product, string input, string expected)
        {
            Assert.Equal(expected, LifecycleAdvisoryGenerator.NormaliseVersion(product, input));
        }
    }
}