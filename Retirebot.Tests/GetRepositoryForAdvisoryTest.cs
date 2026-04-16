using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Retirebot.Functions;
using Retirebot.Helpers;
using Retirebot.Models;
using Retirebot.Models.Azure;

namespace Retirebot.Tests;

public class GetRepositoryForAdvisoryTest
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> settings)
    {
        return new ConfigurationBuilder()
        .AddInMemoryCollection(settings)
        .Build();
    }

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
            [ConfigKeys.App.WorkItemScope] = WorkItemScope.Monolithic.ToString()
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
}