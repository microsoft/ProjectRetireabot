using Microsoft.RetireaBot.Helpers.Settings;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;
using Moq;

namespace Microsoft.RetireaBot.Tests.Helpers.Settings
{
    public class ScanScopeResolverTests
    {
        private const string DeploymentSubscription = "11111111-1111-1111-1111-111111111111";
        private const string OtherSubscription = "22222222-2222-2222-2222-222222222222";
        private const string GroupScope = $"/subscriptions/{DeploymentSubscription}/resourceGroups/az-lifecycle";

        [Fact]
        public void Explicit_NormalizesAndDeduplicatesScopesWithoutExpandingAccess()
        {
            var result = ScanScopeResolver.Resolve(new ScanOptions
            {
                Mode = ScanMode.Explicit,
                Scopes = [GroupScope, GroupScope.ToUpperInvariant(), $"/subscriptions/{OtherSubscription}"]
            }, DeploymentSubscription, []);

            Assert.Collection(result.Scopes,
                group =>
                {
                    Assert.Equal(GroupScope, group.ScopeId);
                    Assert.Equal(DeploymentSubscription, group.SubscriptionId);
                    Assert.Equal("az-lifecycle", group.ResourceGroupName);
                },
                subscription =>
                {
                    Assert.Equal($"/subscriptions/{OtherSubscription}", subscription.ScopeId);
                    Assert.Null(subscription.ResourceGroupName);
                });
        }

        [Theory]
        [InlineData(ScanMode.Explicit)]
        [InlineData(ScanMode.FromRouting)]
        public void RestrictedMode_RejectsEmptyScopes(ScanMode mode)
        {
            Assert.Throws<InvalidOperationException>(() =>
                ScanScopeResolver.Resolve(new ScanOptions { Mode = mode }, DeploymentSubscription, []));
        }

        [Theory]
        [InlineData("")]
        [InlineData("az-lifecycle")]
        [InlineData("/subscriptions/not-a-guid")]
        [InlineData("/subscriptions/ 11111111-1111-1111-1111-111111111111 ")]
        [InlineData("/subscriptions/11111111-1111-1111-1111-111111111111/")]
        [InlineData("/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/")]
        [InlineData("/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/invalid group")]
        [InlineData("/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/invalid.")]
        [InlineData("/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/invalid\n")]
        [InlineData("/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/\u212A")]
        [InlineData("/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.Web/sites/app")]
        [InlineData("/providers/Microsoft.Management/managementGroups/group")]
        public void Explicit_RejectsInvalidScopeEvenAlongsideValidScope(string invalidScope)
        {
            Assert.Throws<InvalidOperationException>(() => ScanScopeResolver.Resolve(new ScanOptions
            {
                Mode = ScanMode.Explicit,
                Scopes = [GroupScope, invalidScope]
            }, DeploymentSubscription, []));
        }

        [Theory]
        [InlineData(ScanMode.FromRouting)]
        [InlineData(ScanMode.DeploymentSubscription)]
        public void NonExplicitMode_RejectsExplicitScopes(ScanMode mode)
        {
            Assert.Throws<InvalidOperationException>(() => ScanScopeResolver.Resolve(new ScanOptions
            {
                Mode = mode,
                Scopes = [GroupScope]
            }, DeploymentSubscription, []));
        }

        [Fact]
        public void FromRouting_DeduplicatesAcrossEnabledBackendsAndIgnoresMonolithicMappings()
        {
            IVendorSettings[] backends =
            [
                Backend(WorkItemScope.PerContainer, Mapping(GroupScope)),
                Backend(WorkItemScope.PerContainer, Mapping(GroupScope.ToUpperInvariant())),
                Backend(WorkItemScope.Monolithic, Mapping(null))
            ];

            var result = ScanScopeResolver.Resolve(
                new ScanOptions { Mode = ScanMode.FromRouting }, DeploymentSubscription, backends);

            Assert.Equal(GroupScope, Assert.Single(result.Scopes).ScopeId);
        }

        [Fact]
        public void FromRouting_PreservesCrossSubscriptionGroupsAndSubscriptionMappings()
        {
            string otherGroup = $"/subscriptions/{OtherSubscription}/resourceGroups/az-lifecycle";
            string subscriptionScope = $"/subscriptions/{OtherSubscription}";

            var result = ScanScopeResolver.Resolve(
                new ScanOptions { Mode = ScanMode.FromRouting },
                DeploymentSubscription,
                [Backend(WorkItemScope.PerContainer,
                    Mapping(GroupScope),
                    Mapping(otherGroup),
                    Mapping(subscriptionScope, AzureContainerType.Subscription))]);

            Assert.Equal(new[] { GroupScope, otherGroup, subscriptionScope },
                result.Scopes.Select(scope => scope.ScopeId));
        }

        [Fact]
        public void Explicit_DoesNotDependOnRoutingOrDeploymentSubscription()
        {
            var result = ScanScopeResolver.Resolve(
                new ScanOptions { Mode = ScanMode.Explicit, Scopes = [GroupScope] },
                "",
                [Backend(WorkItemScope.PerContainer, Mapping(null))]);

            Assert.Equal(GroupScope, Assert.Single(result.Scopes).ScopeId);
        }

        [Theory]
        [InlineData(null, AzureContainerType.ResourceGroup)]
        [InlineData("", AzureContainerType.ResourceGroup)]
        [InlineData(GroupScope, AzureContainerType.Subscription)]
        [InlineData(GroupScope, AzureContainerType.ManagementGroup)]
        public void FromRouting_RejectsMissingScopeIdAndMismatchedTypes(string? scopeId, AzureContainerType type)
        {
            Assert.Throws<InvalidOperationException>(() => ScanScopeResolver.Resolve(
                new ScanOptions { Mode = ScanMode.FromRouting },
                DeploymentSubscription,
                [Backend(WorkItemScope.PerContainer, Mapping(scopeId, type))]));
        }

        [Fact]
        public void DeploymentSubscription_UsesOnlyDeploymentSubscriptionAndIgnoresRouting()
        {
            var result = ScanScopeResolver.Resolve(
                new ScanOptions { Mode = ScanMode.DeploymentSubscription },
                DeploymentSubscription,
                [Backend(WorkItemScope.PerContainer, Mapping(GroupScope))]);

            Assert.Equal($"/subscriptions/{DeploymentSubscription}", Assert.Single(result.Scopes).ScopeId);
        }

        [Fact]
        public void DeploymentSubscription_RejectsInvalidDeploymentSubscription()
        {
            Assert.Throws<InvalidOperationException>(() => ScanScopeResolver.Resolve(
                new ScanOptions { Mode = ScanMode.DeploymentSubscription }, "", []));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AssignReader_DoesNotChangeResolvedScanScopes(bool assignReader)
        {
            var result = ScanScopeResolver.Resolve(new ScanOptions
            {
                Mode = ScanMode.Explicit,
                Scopes = [GroupScope],
                AssignReader = assignReader
            }, DeploymentSubscription, []);

            Assert.Equal(assignReader, result.AssignReader);
            Assert.Equal(GroupScope, Assert.Single(result.Scopes).ScopeId);
        }

        [Fact]
        public void Resolve_RejectsUnknownMode()
        {
            Assert.Throws<InvalidOperationException>(() => ScanScopeResolver.Resolve(
                new ScanOptions { Mode = (ScanMode)999 }, DeploymentSubscription, []));
        }

        private static AzureRepositoryMap Mapping(
            string? scopeId, AzureContainerType type = AzureContainerType.ResourceGroup) => new()
        {
            Name = type == AzureContainerType.Subscription ? scopeId?.Split('/')[2] : "az-lifecycle",
            Type = type,
            Target = "owner/repository",
            ScopeId = scopeId
        };

        private static IVendorSettings Backend(WorkItemScope scope, params AzureRepositoryMap[] mappings)
        {
            var backend = new Mock<IVendorSettings>();
            backend.SetupGet(x => x.WorkItemScope).Returns(scope);
            backend.SetupGet(x => x.TargetContainerMapping).Returns(mappings.ToList());
            return backend.Object;
        }
    }
}
