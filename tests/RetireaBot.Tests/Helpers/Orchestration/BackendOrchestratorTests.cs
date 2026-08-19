using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Domain;
using Microsoft.RetireaBot.Helpers;
using Microsoft.RetireaBot.Helpers.Orchestration;
using Microsoft.RetireaBot.Helpers.Settings;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;

using Moq;

namespace Microsoft.RetireaBot.Tests.Helpers.Orchestration
{
    public class BackendOrchestratorTests
    {
        // Common configuration for orchestration tests; each test can override only the flags it cares about.
        private static IConfiguration BuildConfig(Dictionary<string, string?>? overrides = null)
        {
            var defaults = new Dictionary<string, string?>
            {
                [ConfigKeys.GitHub.TargetRepository] = "github-owner/github-repo",
                [ConfigKeys.AzureDevOps.TargetRepository] = "ado-project",
                [ConfigKeys.App.WorkItemScope] = nameof(WorkItemScope.Monolithic),
                [ConfigKeys.App.CreateParentWorkItems] = "true",
                [ConfigKeys.App.CreateChildWorkItems] = "true",
                [ConfigKeys.App.UseTriageRepoForUnmapped] = "false"
            };

            if (overrides != null)
            {
                foreach (var kvp in overrides)
                {
                    defaults[kvp.Key] = kvp.Value;
                }
            }

            return new ConfigurationBuilder().AddInMemoryCollection(defaults).Build();
        }

        private static Mock<Microsoft.RetireaBot.Helpers.Azure.ManagementClient> BuildManagementClient()
            => new(new HttpClient(new Mock<HttpMessageHandler>().Object)) { CallBase = false };

        private static Advisory CreateAdvisory(string name, string recommendationTypeId)
            => new()
            {
                Id = $"/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Advisor/recommendations/{name}",
                Name = name,
                Type = "Microsoft.Advisor/recommendations",
                Properties = new AdvisoryProperties
                {
                    Category = "HighAvailability",
                    Impact = "High",
                    ImpactedField = "Microsoft.Web/sites",
                    ImpactedValue = "resource-name",
                    RecommendationTypeId = recommendationTypeId,
                    ShortDescription = new ShortDescription
                    {
                        Problem = "Problem",
                        Solution = "Solution"
                    },
                    ExtendedProperties = new ExtendedProperties
                    {
                        RetirementDate = "2026-01-01",
                        RetirementFeatureName = "Feature"
                    },
                    ResourceMetadata = new ResourceMetadata
                    {
                        ResourceId = "/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Web/sites/resource-name"
                    }
                }
            };

        private static WorkItem CreateWorkItem(string id, string title)
            => new()
            {
                Id = id,
                Number = int.Parse(id),
                Title = title,
                State = WorkItemState.Open,
                Labels = [],
                Assignees = []
            };

        // Build the real orchestrator with mocked backends so the tests exercise the production routing logic.
        private static BackendOrchestrator CreateOrchestrator(
            IConfiguration config,
            params Mock<IWorkItemClient>[] workItemClients)
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
            var managementClient = BuildManagementClient();
            var vendorSettings = new VendorSettingsProvider(config, new[] { WorkItemBackend.GitHub, WorkItemBackend.AzureDevOps }, new[] { DataSinkBackend.NoOp });

            return new BackendOrchestrator(loggerFactory, config, workItemClients.Select(x => x.Object), vendorSettings, managementClient.Object);
        }

        [Fact]
        // Verifies that one orchestration run produces one output per registered backend.
        public async Task RunAsync_ReturnsDistinctBackendOutputsForEachRegisteredClient()
        {
            var config = BuildConfig();
            var githubClient = new Mock<IWorkItemClient>();
            githubClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.GitHub);
            githubClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            githubClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(new List<(Advisory, WorkItem)>
                {
                    (CreateAdvisory("github-advisory", "type-g"), CreateWorkItem("1", "GitHub child")),
                    (CreateAdvisory("github-advisory-2", "type-g-2"), CreateWorkItem("2", "GitHub child 2"))
                });
            githubClient.Setup(x => x.FindOrCreateParentAsync(It.IsAny<string>(), It.IsAny<Advisory>(), It.IsAny<Dictionary<string, List<WorkItem>>>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new ParentWorkItemResult { Action = ParentWorkItemAction.Created, ChildCount = 1, RecommendationTypeId = "type-g", WorkItem = CreateWorkItem("10", "GitHub parent") });

            var adoClient = new Mock<IWorkItemClient>();
            adoClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.AzureDevOps);
            adoClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            adoClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(new List<(Advisory, WorkItem)>
                {
                    (CreateAdvisory("ado-advisory", "type-a"), CreateWorkItem("3", "ADO child")),
                    (CreateAdvisory("ado-advisory-2", "type-a-2"), CreateWorkItem("4", "ADO child 2"))
                });
            adoClient.Setup(x => x.FindOrCreateParentAsync(It.IsAny<string>(), It.IsAny<Advisory>(), It.IsAny<Dictionary<string, List<WorkItem>>>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new ParentWorkItemResult { Action = ParentWorkItemAction.Created, ChildCount = 1, RecommendationTypeId = "type-a", WorkItem = CreateWorkItem("20", "ADO parent") });

            var sut = CreateOrchestrator(config, githubClient, adoClient);

            var outputs = await sut.RunAsync(new List<Advisory> { CreateAdvisory("github-advisory", "type-g"), CreateAdvisory("ado-advisory", "type-a") }, whatIf: false, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, outputs.Count);
            Assert.Contains(outputs, x => x.BackendName == nameof(WorkItemBackend.GitHub));
            Assert.Contains(outputs, x => x.BackendName == nameof(WorkItemBackend.AzureDevOps));
            Assert.All(outputs, x => Assert.Equal(GetRetirementsResult.Success, x.Status));
        }

        [Fact]
        // Ensures parent creation receives only the children created for that same backend.
        public async Task RunAsync_PassesOnlyThatBackendChildItemsToParentCreation()
        {
            var config = BuildConfig();
            var githubParentCapture = new List<Dictionary<string, List<WorkItem>>>();
            var adoParentCapture = new List<Dictionary<string, List<WorkItem>>>();

            var githubClient = new Mock<IWorkItemClient>();
            githubClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.GitHub);
            githubClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            githubClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(new List<(Advisory, WorkItem)> { (CreateAdvisory("github-advisory", "type-g"), CreateWorkItem("1", "GitHub child")) });
            githubClient.Setup(x => x.FindOrCreateParentAsync(It.IsAny<string>(), It.IsAny<Advisory>(), It.IsAny<Dictionary<string, List<WorkItem>>>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, Advisory, Dictionary<string, List<WorkItem>>, string, bool>((_, _, childItemsByRepo, _, _) => githubParentCapture.Add(childItemsByRepo))
                .ReturnsAsync(new ParentWorkItemResult { Action = ParentWorkItemAction.Created, ChildCount = 1, RecommendationTypeId = "type-g", WorkItem = CreateWorkItem("10", "GitHub parent") });

            var adoClient = new Mock<IWorkItemClient>();
            adoClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.AzureDevOps);
            adoClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            adoClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(new List<(Advisory, WorkItem)> { (CreateAdvisory("ado-advisory", "type-a"), CreateWorkItem("2", "ADO child")) });
            adoClient.Setup(x => x.FindOrCreateParentAsync(It.IsAny<string>(), It.IsAny<Advisory>(), It.IsAny<Dictionary<string, List<WorkItem>>>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, Advisory, Dictionary<string, List<WorkItem>>, string, bool>((_, _, childItemsByRepo, _, _) => adoParentCapture.Add(childItemsByRepo))
                .ReturnsAsync(new ParentWorkItemResult { Action = ParentWorkItemAction.Created, ChildCount = 1, RecommendationTypeId = "type-a", WorkItem = CreateWorkItem("20", "ADO parent") });

            var sut = CreateOrchestrator(config, githubClient, adoClient);

            await sut.RunAsync(new List<Advisory> { CreateAdvisory("github-advisory", "type-g"), CreateAdvisory("ado-advisory", "type-a") }, whatIf: false, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Single(githubParentCapture);
            Assert.Single(adoParentCapture);
            Assert.Single(githubParentCapture[0]);
            Assert.Single(adoParentCapture[0]);
            Assert.Equal("GitHub child", githubParentCapture[0].Values.Single().Single().Title);
            Assert.Equal("ADO child", adoParentCapture[0].Values.Single().Single().Title);
        }

        [Fact]
        // Guards against accidental serialization of backend work by proving both backends start before either finishes.
        public async Task RunAsync_StartsBothBackendsBeforeEitherCompletes()
        {
            var config = BuildConfig();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = 0;
            var startedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var githubClient = new Mock<IWorkItemClient>();
            githubClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.GitHub);
            githubClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .Returns(async () =>
                {
                    if (Interlocked.Increment(ref started) == 2)
                    {
                        startedSignal.TrySetResult();
                    }

                    await release.Task;
                    return new Dictionary<string, WorkItem>();
                });
            githubClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync([]);

            var adoClient = new Mock<IWorkItemClient>();
            adoClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.AzureDevOps);
            adoClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .Returns(async () =>
                {
                    if (Interlocked.Increment(ref started) == 2)
                    {
                        startedSignal.TrySetResult();
                    }

                    await release.Task;
                    return new Dictionary<string, WorkItem>();
                });
            adoClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync([]);

            var sut = CreateOrchestrator(config, githubClient, adoClient);
            var task = sut.RunAsync(new List<Advisory> { CreateAdvisory("advisory-a", "type-a") }, whatIf: false, cancellationToken: TestContext.Current.CancellationToken);

            await startedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(2, Volatile.Read(ref started));

            release.TrySetResult();
            await task;
        }

        [Fact]
        // Confirms the aggregate status mapping: all failures become Failure, mixed results become Partial.
        public async Task RunAsync_MarksAllFailedBackendAsFailureAndMixedResultsAsPartial()
        {
            var config = BuildConfig();

            var githubClient = new Mock<IWorkItemClient>();
            githubClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.GitHub);
            githubClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            githubClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync([]);

            var adoClient = new Mock<IWorkItemClient>();
            adoClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.AzureDevOps);
            adoClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            adoClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(new List<(Advisory, WorkItem)>
                {
                    (CreateAdvisory("ado-created", "type-a"), CreateWorkItem("1", "ADO child"))
                });

            var sut = CreateOrchestrator(config, githubClient, adoClient);
            var outputs = await sut.RunAsync(new List<Advisory>
            {
                CreateAdvisory("a", "type-a"),
                CreateAdvisory("b", "type-b")
            }, whatIf: false, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains(outputs, x => x.BackendName == nameof(WorkItemBackend.GitHub) && x.Status == GetRetirementsResult.Failure);
            Assert.Contains(outputs, x => x.BackendName == nameof(WorkItemBackend.AzureDevOps) && x.Status == GetRetirementsResult.Partial);
        }

        [Fact]
        // Ensures one backend exception is isolated and does not block the other backend from finishing.
        public async Task RunAsync_ExceptionInOneBackend_DoesNotPreventOtherBackendFromCompleting()
        {
            var config = BuildConfig();

            var githubClient = new Mock<IWorkItemClient>();
            githubClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.GitHub);
            githubClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("GitHub exploded"));

            var adoClient = new Mock<IWorkItemClient>();
            adoClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.AzureDevOps);
            adoClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            adoClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(new List<(Advisory, WorkItem)> { (CreateAdvisory("ado-advisory", "type-a"), CreateWorkItem("9", "ADO child")) });

            var sut = CreateOrchestrator(config, githubClient, adoClient);

            var outputs = await sut.RunAsync(new List<Advisory> { CreateAdvisory("advisory-a", "type-a") }, whatIf: false, TestContext.Current.CancellationToken);

            Assert.Contains(outputs, x => x.BackendName == nameof(WorkItemBackend.GitHub) && x.Status == GetRetirementsResult.Failure && x.Error == "GitHub exploded");
            Assert.Contains(outputs, x => x.BackendName == nameof(WorkItemBackend.AzureDevOps) && x.Status == GetRetirementsResult.Success);
        }

        [Fact]
        // Verifies the toggle that skips parent work item generation entirely.
        public async Task RunAsync_CreateParentWorkItemsFalse_SkipsParentCreation()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.CreateParentWorkItems] = "false"
            });

            var githubClient = new Mock<IWorkItemClient>();
            githubClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.GitHub);
            githubClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            githubClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(new List<(Advisory, WorkItem)> { (CreateAdvisory("github-advisory", "type-g"), CreateWorkItem("1", "GitHub child")) });

            var sut = CreateOrchestrator(config, githubClient);

            var outputs = await sut.RunAsync(new List<Advisory> { CreateAdvisory("github-advisory", "type-g") }, whatIf: false, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Single(outputs);
            Assert.Equal(GetRetirementsResult.Success, outputs[0].Status);
            Assert.Empty(outputs[0].Parents);
            Assert.Single(outputs[0].Created);
        }

        [Fact]
        // Verifies the toggle that skips child work item creation while still allowing the run to complete.
        public async Task RunAsync_CreateChildWorkItemsFalse_SkipsChildCreation()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.CreateChildWorkItems] = "false"
            });

            var githubClient = new Mock<IWorkItemClient>();
            githubClient.SetupGet(x => x.Backend).Returns(WorkItemBackend.GitHub);
            githubClient.Setup(x => x.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            githubClient.Setup(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync([]);

            var sut = CreateOrchestrator(config, githubClient);

            var outputs = await sut.RunAsync(new List<Advisory> { CreateAdvisory("github-advisory", "type-g") }, whatIf: false, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Single(outputs);
            Assert.Equal(GetRetirementsResult.Success, outputs[0].Status);
            Assert.Empty(outputs[0].Created);
            githubClient.Verify(x => x.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        }
    }
}
