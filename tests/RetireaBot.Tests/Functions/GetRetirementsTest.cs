using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Microsoft.RetireaBot.Functions;
using Microsoft.RetireaBot.Helpers;
using Microsoft.RetireaBot.Helpers.Lifecycle;
using Microsoft.RetireaBot.Helpers.Orchestration;
using Microsoft.RetireaBot.Helpers.Settings;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;

using Microsoft.RetireaBot.Contracts;
using Microsoft.RetireaBot.Domain;

namespace Microsoft.RetireaBot.Tests.Functions
{
    public class GetRetirementsTest
    {
        private static IConfiguration BuildConfig(Dictionary<string, string?> settings) =>
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        private static Advisory CreateAdvisory(string name = "test-advisory-1", string typeId = "type-1", string impact = "High") =>
            TestData.CreateAdvisory(name, typeId, impact);

        private static (GetRetirements function, Mock<IWorkItemClient> mockWorkItem, Mock<Microsoft.RetireaBot.Helpers.Azure.ManagementClient> mockMgmt) BuildFunction(
            Dictionary<string, string?>? configOverrides = null)
        {
            var defaults = new Dictionary<string, string?>
            {
                [ConfigKeys.GitHub.TargetRepository] = "owner/repo",
                [ConfigKeys.AzureDevOps.TargetRepository] = "AdoProject",
                [ConfigKeys.App.WorkItemScope] = "Monolithic",
                [ConfigKeys.App.AssignGitHubCopilot] = "false",
                [ConfigKeys.App.CreateParentWorkItems] = "true",
                [ConfigKeys.App.CreateChildWorkItems] = "true",
                [ConfigKeys.App.HTTPEndpointEnable] = "true",
                [ConfigKeys.App.HTTPEndpointOutput] = "true",
                [ConfigKeys.App.HTTPEndpointWhatIf] = "true",
            };

            if (configOverrides != null)
            {
                foreach (var kvp in configOverrides)
                    defaults[kvp.Key] = kvp.Value;
            }

            var config = new ConfigurationBuilder().AddInMemoryCollection(defaults).Build();
            var loggerFactory = LoggerFactory.Create(b => b.AddDebug());

            var mockHttpClient = new HttpClient(new Mock<HttpMessageHandler>().Object)
            {
                BaseAddress = new Uri("https://management.azure.com/")
            };
            var mockMgmt = new Mock<Microsoft.RetireaBot.Helpers.Azure.ManagementClient>(mockHttpClient) { CallBase = false };
            var mockWorkItem = new Mock<IWorkItemClient>();
            mockWorkItem.SetupGet(c => c.Backend).Returns(WorkItemBackend.GitHub);

            var vendorSettings = new VendorSettingsProvider(config, new[] { WorkItemBackend.GitHub, WorkItemBackend.AzureDevOps }, new[] { DataSinkBackend.NoOp });
            var orchestrator = new BackendOrchestrator(loggerFactory, config, new[] { mockWorkItem.Object }, vendorSettings, mockMgmt.Object);
            var sinkOrchestrator = new Mock<IDataSinkOrchestrator>();
            sinkOrchestrator.Setup(x => x.RunAsync(It.IsAny<List<Advisory>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DataSinkOutputResult>());
            var lifecycleClient = new LifecycleClient(new HttpClient(new Mock<HttpMessageHandler>().Object), loggerFactory);
            var registry = new ResponseProjectorRegistry([]);

            var function = new GetRetirements(loggerFactory, config, mockMgmt.Object, orchestrator, sinkOrchestrator.Object, lifecycleClient, registry);

            return (function, mockWorkItem, mockMgmt);
        }

        private static (Microsoft.RetireaBot.Helpers.Azure.ManagementClient client, Mock<HttpMessageHandler> handler) BuildMockManagementClient()
        {
            var handler = new Mock<HttpMessageHandler>();
            var httpClient = new HttpClient(handler.Object)
            {
                BaseAddress = new Uri("https://management.azure.com/")
            };

            return (new Microsoft.RetireaBot.Helpers.Azure.ManagementClient(httpClient), handler);
        }

        private static void SetupHttpResource(Mock<HttpMessageHandler> handler, object responseBody)
        {
            handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(
                    JsonSerializer.Serialize(responseBody),
                    System.Text.Encoding.UTF8,
                    "application/json"
                )
            });
        }

        private static void SetupSubscriptionAndQueryResponse(
            Mock<HttpMessageHandler> handler, int advisoryCount = 1)
        {
            handler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                // subscriptions response
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { value = new[] { new { subscriptionId = "sub-1" } } }),
                        System.Text.Encoding.UTF8, "application/json")
                })
                // resource graph query response
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(TestData.CreateRetirementQueryResponse(1)), System.Text.Encoding.UTF8, "application/json")
                });
        }

        [Fact]
        public async Task GetRetirementsASync_NoSubscriptions_ReturnsFailure()
        {
            var (function, mockWorkItem, mockMgmt) = BuildFunction();
            mockMgmt.Setup(m => m.GetSubscriptionsAsync()).ReturnsAsync(Array.Empty<string>());

            var result = await function.GetRetirementsASync();

            Assert.Equal(GetRetirementsResult.Failure, result.Result);
            Assert.Contains("No subscriptions", result.Description);
        }

        [Fact]
        public async Task GetRetirementsASync_WhatIf_DoesNotCreateRealIssues()
        {
            var (function, mockWorkItem, mockMgmt) = BuildFunction();
            var advisory = CreateAdvisory();

            mockMgmt.Setup(m => m.GetSubscriptionsAsync()).ReturnsAsync(new[] { "sub-1" });
            mockMgmt.Setup(m => m.RunQueryAsync<RetirementData>("sub-1", It.IsAny<string>()))
                .ReturnsAsync(new QueryResult<RetirementData>
                {
                    Data = new List<RetirementData>() { RetirementDataFromAdvisory(advisory) },
                    Length = 1
                });

            mockWorkItem.Setup(m => m.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), "owner/repo"))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            mockWorkItem.Setup(m => m.CreateBatchAsync(It.IsAny<List<Advisory>>(), "owner/repo", false, true))
                .ReturnsAsync(new List<(Advisory, WorkItem)>
                {
                    (advisory, new WorkItem {Id = "", Number = 0, Title = "WhatIf Issue"})
                });

            mockWorkItem.Setup(m => m.FindOrCreateParentAsync(It.IsAny<string>(), It.IsAny<Advisory>(), It.IsAny<Dictionary<string, List<WorkItem>>>(), "owner/repo", true))
                .ReturnsAsync(new ParentWorkItemResult
                {
                    Action = ParentWorkItemAction.Created,
                    ChildCount = 1,
                    RecommendationTypeId = "type-1",
                    WorkItem = new WorkItem { Id = "", Number = 0, Title = "WhatIf Parent" }
                });

            var result = await function.GetRetirementsASync(whatIf: true);

            Assert.Equal(GetRetirementsResult.Success, result.Result);
            Assert.True(result.WhatIf);

            mockWorkItem.Verify(m => m.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), true), Times.Once);
            mockWorkItem.Verify(m => m.FindOrCreateParentAsync(It.IsAny<string>(), It.IsAny<Advisory>(), It.IsAny<Dictionary<string, List<WorkItem>>>(), It.IsAny<string>(), true), Times.Once);
        }

        [Fact]
        public async Task GetRetirementAsync_WhatIf_ResponseFlagIsSet()
        {
            var (function, _, mockMgmt) = BuildFunction();
            mockMgmt.Setup(m => m.GetSubscriptionsAsync()).ReturnsAsync(Array.Empty<string>());

            var result = await function.GetRetirementsASync(whatIf: true);

            Assert.True(result.WhatIf);
        }

        [Fact]
        public async Task GetRetirementsAsync_WithAdvisories_CreatesWorkItems()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.GitHub.TargetRepository] = "example/repo",
                [ConfigKeys.AzureDevOps.TargetRepository] = "AdoProject",
                [ConfigKeys.App.WorkItemScope] = WorkItemScope.Monolithic.ToString(),
                [ConfigKeys.App.CreateChildWorkItems] = "true",
            });

            var (mgmtClient, handler) = BuildMockManagementClient();

            SetupSubscriptionAndQueryResponse(handler, advisoryCount: 1);

            var mockWorkItemClient = new Mock<IWorkItemClient>();
            mockWorkItemClient.SetupGet(c => c.Backend).Returns(WorkItemBackend.GitHub);
            mockWorkItemClient
                .Setup(c => c.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), "example/repo"))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            mockWorkItemClient
                .Setup(c => c.CreateBatchAsync(It.IsAny<List<Advisory>>(), "example/repo", false, false))
                .ReturnsAsync(new List<(Advisory, WorkItem)>());

            var loggerFactory = LoggerFactory.Create(b => b.AddDebug());

            var vendorSettings = new VendorSettingsProvider(config, new[] { WorkItemBackend.GitHub, WorkItemBackend.AzureDevOps }, new[] { DataSinkBackend.NoOp });
            var orchestrator = new BackendOrchestrator(loggerFactory, config, new[] { mockWorkItemClient.Object }, vendorSettings, mgmtClient);
            var sinkOrchestrator = new Mock<IDataSinkOrchestrator>();
            sinkOrchestrator.Setup(x => x.RunAsync(It.IsAny<List<Advisory>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DataSinkOutputResult>());
            var lifecycleClient = new LifecycleClient(new HttpClient(new Mock<HttpMessageHandler>().Object), loggerFactory);
            var registry = new ResponseProjectorRegistry([]);
            var sut = new GetRetirements(loggerFactory, config, mgmtClient, orchestrator, sinkOrchestrator.Object, lifecycleClient, registry);

            await sut.GetRetirementsASync();

            // Assert - should have tried to create work items
            mockWorkItemClient.Verify(
                c => c.CreateBatchAsync(It.Is<List<Advisory>>(a => a.Count == 1), "example/repo", false, false),
                Times.Once);
        }

        [Fact]
        public async Task GetRetirementsAsync_ExistingIssues_SkipsCreation()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.GitHub.TargetRepository] = "example/repo",
                [ConfigKeys.AzureDevOps.TargetRepository] = "AdoProject",
                [ConfigKeys.App.WorkItemScope] = WorkItemScope.Monolithic.ToString(),
                [ConfigKeys.App.CreateChildWorkItems] = "true",
            });

            var (mgmtClient, handler) = BuildMockManagementClient();

            SetupSubscriptionAndQueryResponse(handler, advisoryCount: 1);

            var mockWorkItemClient = new Mock<IWorkItemClient>();
            mockWorkItemClient.SetupGet(c => c.Backend).Returns(WorkItemBackend.GitHub);

            mockWorkItemClient
                .Setup(c => c.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), "example/repo"))
                .ReturnsAsync(new Dictionary<string, WorkItem>
                {
                    ["rec-1"] = new WorkItem { Id = "1", Number = 1, Title = "Existing issue" }
                });

            mockWorkItemClient
                .Setup(c => c.CreateBatchAsync(It.IsAny<List<Advisory>>(), "example/repo", false, false))
                .ReturnsAsync(new List<(Advisory, WorkItem)>());

            var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
            var vendorSettings = new VendorSettingsProvider(config, new[] { WorkItemBackend.GitHub, WorkItemBackend.AzureDevOps }, new[] { DataSinkBackend.NoOp });
            var orchestrator = new BackendOrchestrator(loggerFactory, config, new[] { mockWorkItemClient.Object }, vendorSettings, mgmtClient);
            var sinkOrchestrator = new Mock<IDataSinkOrchestrator>();
            sinkOrchestrator.Setup(x => x.RunAsync(It.IsAny<List<Advisory>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DataSinkOutputResult>());
            var lifecycleClient = new LifecycleClient(new HttpClient(new Mock<HttpMessageHandler>().Object), loggerFactory);
            var registry = new ResponseProjectorRegistry([]);

            var sut = new GetRetirements(loggerFactory, config, mgmtClient, orchestrator, sinkOrchestrator.Object, lifecycleClient, registry);

            await sut.GetRetirementsASync();

            mockWorkItemClient.Verify(
                c => c.CreateBatchAsync(
                    It.Is<List<Advisory>>(a => a.Count == 0),
                    "example/repo",
                    false,
                    false
                ),
                Times.Once
            );
        }

        private static RetirementData RetirementDataFromAdvisory(Advisory advisory)
        {
            return new RetirementData
            {
                Id = advisory.Id,
                Name = advisory.Name,
                Type = advisory.Type,
                SubscriptionId = advisory.GetSubscriptionId(),
                ResourceGroup = advisory.GetResourceGroupName(),
                Location = "eastus",
                ResourceId = advisory.Properties.ResourceMetadata.ResourceId,
                ServiceID = advisory.Properties.RecommendationTypeId,
                Impact = advisory.Properties.Impact,
                Category = advisory.Properties.Category,
                ImpactedField = advisory.Properties.ImpactedField,
                ImpactedValue = advisory.Properties.ImpactedValue,
                MaturityLevel = advisory.Properties.ExtendedProperties.MaturityLevel,
                RecommendationOfferingId = advisory.Properties.ExtendedProperties.RecommendationOfferingId,
                LastUpdated = advisory.Properties.LastUpdated.ToString("o"),
                RetirementDate = advisory.Properties.ExtendedProperties.RetirementDate,
                RetirementFeatureName = advisory.Properties.ExtendedProperties.RetirementFeatureName,
                ShortDescriptionProblem = advisory.Properties.ShortDescription.Problem,
                ShortDescriptionSolution = advisory.Properties.ShortDescription.Solution
            };
        }
    }
}