using Moq;
using Moq.Protected;

namespace Retirebot.Tests
{
    public class ManagementClientTest
    {
        [Fact]
        public async Task ManagementClient_MockExample()
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("""{ "value": [{ "subscriptionId": "sub-123" }] }""", System.Text.Encoding.UTF8, "application/json")
            });

            var httpClient = new HttpClient(mockHandler.Object)
            {
                BaseAddress = new Uri("https://management.azure.com/")
            };

            var client = new Helpers.Azure.ManagementClient(httpClient);
            var subs = await client.GetSubscriptionsAsync();

            Assert.Single(subs);
            Assert.Equal("sub-123", subs[0]);
        }
    }
}