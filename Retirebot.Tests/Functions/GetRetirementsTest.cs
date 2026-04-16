using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Octokit;
using Retirebot.Functions;
using Retirebot.Helpers;
using Retirebot.Models;
using Retirebot.Models.Azure;

namespace Retirebot.Tests.Functions
{
    public class GetRepositoryForAdvisoryTest
    {
        private static IConfiguration BuildConfig(Dictionary<string, string?> settings) =>
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        private static (Retirebot.Helpers.Azure.ManagementClient client, Mock<HttpMessageHandler> handler) BuildMockManagementClient()
        {
            var handler = new Mock<HttpMessageHandler>();
            var httpClient = new HttpClient(handler.Object)
            {
                BaseAddress = new Uri("https://management.azure.com/")
            };

            return (new Retirebot.Helpers.Azure.ManagementClient(httpClient), handler);
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

        [Fact]
        public async Task GetRetirementsAsync_NoSubscriptions_DoesNotCreateWorkItems()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.TargetRepository] = "example/repo",
                [ConfigKeys.App.WorkItemScope] = WorkItemScope.Monolithic.ToString(),
            });

            var (mgmtClient, handler) = BuildMockManagementClient();

            SetupHttpResource(handler, new { value = Array.Empty<object>() });

            var mockWorkItemClient = new Mock<IWorkItemClient>();
            var loggerFactory = LoggerFactory.Create(b => b.AddDebug());

            var sut = new GetRetirements(loggerFactory, config, mgmtClient, mockWorkItemClient.Object);

            await sut.GetRetirementsASync();

            // assert - no work items should have been created
            mockWorkItemClient.Verify(
                c => c.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never
            );
        }

        [Fact]
        public async Task GetRetirementsAsync_WithAdvisories_CreatesWorkItems()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.TargetRepository] = "example/repo",
                [ConfigKeys.App.WorkItemScope] = WorkItemScope.Monolithic.ToString(),
                [ConfigKeys.App.CreateChildWorkItems] = "true",
            });

            var (mgmtClient, handler) = BuildMockManagementClient();

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

            var mockWorkItemClient = new Mock<IWorkItemClient>();
            mockWorkItemClient
                .Setup(c => c.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), "example/repo"))
                .ReturnsAsync(new Dictionary<string, WorkItem>());
            mockWorkItemClient
                .Setup(c => c.CreateBatchAsync(It.IsAny<List<Advisory>>(), "example/repo", false))
                .ReturnsAsync(new List<(Advisory, WorkItem)>());

            var loggerFactory = LoggerFactory.Create(b => b.AddDebug());

            var sut = new GetRetirements(loggerFactory, config, mgmtClient, mockWorkItemClient.Object);

            await sut.GetRetirementsASync();

            // Assert - should have tried to create work items
            mockWorkItemClient.Verify(
                c => c.CreateBatchAsync(It.Is<List<Advisory>>(a => a.Count == 1), "example/repo", false),
                Times.Once);
        }

        [Fact]
        public async Task GetRetirementsAsync_ExistingIssues_SkipsCreation()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.TargetRepository] = "example/repo",
                [ConfigKeys.App.WorkItemScope] = WorkItemScope.Monolithic.ToString(),
                [ConfigKeys.App.CreateChildWorkItems] = "true",
            });

            var (mgmtClient, handler) = BuildMockManagementClient();

            handler.Protected()
               .SetupSequence<Task<HttpResponseMessage>>("SendAsync",
               ItExpr.IsAny<HttpRequestMessage>(),
               ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(new HttpResponseMessage
               {
                   StatusCode = HttpStatusCode.OK,
                   Content = new StringContent(
                    JsonSerializer.Serialize(new { value = new[] { new { subscriptionId = "sub-1" } } }),
                    System.Text.Encoding.UTF8, "application/json"
                   )
               })
               .ReturnsAsync(new HttpResponseMessage
               {
                   StatusCode = HttpStatusCode.OK,
                   Content = new StringContent(
                        JsonSerializer.Serialize(TestData.CreateRetirementQueryResponse(1)),
                        System.Text.Encoding.UTF8, "application/json")
               });

            var mockWorkItemClient = new Mock<IWorkItemClient>();

            mockWorkItemClient
                .Setup(c => c.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), "example/repo"))
                .ReturnsAsync(new Dictionary<string, WorkItem>
                {
                    ["rec-1"] = new WorkItem { Id = "1", Number = 1, Title = "Existing issue" }
                });

            mockWorkItemClient
                .Setup(c => c.CreateBatchAsync(It.IsAny<List<Advisory>>(), "example/repo", false))
                .ReturnsAsync(new List<(Advisory, WorkItem)>());

            var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
            var sut = new GetRetirements(loggerFactory, config, mgmtClient, mockWorkItemClient.Object);

            await sut.GetRetirementsASync();

            mockWorkItemClient.Verify(
                c => c.CreateBatchAsync(
                    It.Is<List<Advisory>>(a => a.Count == 0),
                    "example/repo",
                    false
                ),
                Times.Once
            );
        }
    }
}