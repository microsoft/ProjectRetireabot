using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.RetireaBot.Helpers.Settings;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;

namespace Microsoft.RetireaBot.Tests.Helpers.Settings
{
    public class ScanConfigurationTests
    {
        private const string Subscription = "11111111-1111-1111-1111-111111111111";
        private const string Scope = $"/subscriptions/{Subscription}/resourceGroups/rg-one";

        private static IConfiguration Config(params (string key, string? value)[] settings) =>
            new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(x => x.key, x => x.value)).Build();

        [Fact]
        public void OmittedMode_PreservesLegacyDiscovery()
        {
            Assert.Null(ScanConfiguration.Load(Config(), []));
            Assert.Null(ScanConfiguration.Load(Config((ConfigKeys.Scan.Mode, ""), (ConfigKeys.Scan.Scopes, "[]"),
                (ConfigKeys.Scan.ResolvedScopes, "[]"), (ConfigKeys.Scan.AssignReader, "true")), []));
        }

        [Theory]
        [InlineData(ConfigKeys.Scan.Scopes, "[\"/subscriptions/11111111-1111-1111-1111-111111111111\"]")]
        [InlineData(ConfigKeys.Scan.AssignReader, "false")]
        [InlineData(ConfigKeys.Scan.Mode, "0")]
        [InlineData(ConfigKeys.Scan.Mode, "Unknown")]
        public void InvalidConfiguration_FailsWithoutFallback(string key, string value)
        {
            Assert.Throws<InvalidOperationException>(() => ScanConfiguration.Load(Config((key, value)), []));
        }

        [Theory]
        [InlineData("null", typeof(InvalidOperationException))]
        [InlineData("\"not-an-array\"", typeof(JsonException))]
        [InlineData("[null]", typeof(InvalidOperationException))]
        [InlineData("[]", typeof(InvalidOperationException))]
        public void Explicit_InvalidScopesFail(string scopes, Type exceptionType)
        {
            Assert.Throws(exceptionType, () => ScanConfiguration.Load(Config(
                (ConfigKeys.Scan.Mode, "Explicit"), (ConfigKeys.Scan.Scopes, scopes)), []));
        }

        [Fact]
        public void Explicit_ExternalRbacStillRestrictsScopesAndChecksDeploymentDrift()
        {
            var settings = Config((ConfigKeys.Scan.Mode, "Explicit"),
                (ConfigKeys.Scan.Scopes, JsonSerializer.Serialize(new[] { Scope.ToUpperInvariant(), Scope })),
                (ConfigKeys.Scan.ResolvedScopes, JsonSerializer.Serialize(new[] { Scope })),
                (ConfigKeys.Scan.AssignReader, "false"));
            var resolved = ScanConfiguration.Load(settings, []);
            Assert.NotNull(resolved);
            Assert.False(resolved.AssignReader);
            Assert.Equal(Scope, Assert.Single(resolved.Scopes).ScopeId);

            settings[ConfigKeys.Scan.ResolvedScopes] = JsonSerializer.Serialize(new[] { $"/subscriptions/{Subscription}" });
            Assert.Throws<InvalidOperationException>(() => ScanConfiguration.Load(settings, []));
        }

        [Fact]
        public void FromRouting_NormalizesScopeOnlyMappingsAcrossEnabledBackends()
        {
            var settings = Config(
                (ConfigKeys.Scan.Mode, "FromRouting"),
                (ConfigKeys.Scan.ResolvedScopes, JsonSerializer.Serialize(new[] { Scope })),
                (ConfigKeys.GitHub.TargetRepository, "owner/triage"),
                (ConfigKeys.GitHub.WorkItemScope, "PerContainer"),
                (ConfigKeys.GitHub.TargetContainerMapping, JsonSerializer.Serialize(new[]
                {
                    new { scopeId = Scope.ToUpperInvariant(), target = "owner/one" }
                })));
            var provider = new VendorSettingsProvider(settings, [WorkItemBackend.GitHub], []);
            var vendor = provider.For(WorkItemBackend.GitHub);
            var resolved = ScanConfiguration.Load(settings, [vendor]);
            Assert.Equal(Scope, Assert.Single(resolved!.Scopes).ScopeId);
            Assert.Equal("rg-one", Assert.Single(vendor.TargetContainerMapping).Name);
            Assert.Equal(AzureContainerType.ResourceGroup, vendor.TargetContainerMapping[0].Type);
        }

        [Theory]
        [InlineData("different", "ResourceGroup")]
        [InlineData("rg-one", "Subscription")]
        public void ScopedMapping_RejectsContradictoryMetadata(string name, string type)
        {
            var settings = Config((ConfigKeys.GitHub.TargetRepository, "owner/triage"),
                (ConfigKeys.GitHub.TargetContainerMapping, JsonSerializer.Serialize(new[]
                {
                    new { scopeId = Scope, name, type, target = "owner/one" }
                })));
            Assert.Throws<InvalidOperationException>(() => new VendorSettingsProvider(settings, [WorkItemBackend.GitHub], []));
        }

        [Fact]
        public void Explicit_AcceptsConfigurationArray()
        {
            var resolved = ScanConfiguration.Load(Config((ConfigKeys.Scan.Mode, "Explicit"), ($"{ConfigKeys.Scan.Scopes}:0", Scope)), []);
            Assert.Equal(Scope, Assert.Single(resolved!.Scopes).ScopeId);
        }

        [Fact]
        public void DeploymentSubscription_RequiresAndUsesDeploymentSubscription()
        {
            var settings = Config((ConfigKeys.Scan.Mode, "DeploymentSubscription"));
            Assert.Throws<InvalidOperationException>(() => ScanConfiguration.Load(settings, []));
            settings[ConfigKeys.Scan.DeploymentSubscriptionId] = Subscription;
            Assert.Equal($"/subscriptions/{Subscription}", Assert.Single(ScanConfiguration.Load(settings, [])!.Scopes).ScopeId);
        }
    }
}
