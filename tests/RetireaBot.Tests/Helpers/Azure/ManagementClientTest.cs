using System.Net;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Moq;
using Moq.Protected;

namespace Microsoft.RetireaBot.Tests.Helpers.Azure
{
    public class ManagementClientTests
    {
        private static Microsoft.RetireaBot.Helpers.Azure.ManagementClient BuildClient(Mock<HttpMessageHandler> handler)
        {
            var httpClient = new HttpClient(handler.Object)
            {
                BaseAddress = new Uri("https://management.azure.com/")
            };
            return new Microsoft.RetireaBot.Helpers.Azure.ManagementClient(httpClient);
        }

        private static void SetupResponse(Mock<HttpMessageHandler> handler, HttpStatusCode status, string content)
        {
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = status,
                    Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
                });
        }

        [Fact]
        public async Task GetSubscriptionsAsync_ReturnsSubscriptions()
        {
            var handler = new Mock<HttpMessageHandler>();
            SetupResponse(handler, HttpStatusCode.OK,
                """{ "value": [{ "subscriptionId": "sub-123" }] }""");

            var client = BuildClient(handler);
            var subs = await client.GetSubscriptionsAsync();

            Assert.Single(subs);
            Assert.Equal("sub-123", subs[0]);
        }


        [Fact]
        public async Task GetSubscriptionsAsync_MultipleSubscriptions_ReturnsAll()
        {
            var handler = new Mock<HttpMessageHandler>();
            SetupResponse(handler, HttpStatusCode.OK,
                """{ "value": [{ "subscriptionId": "sub-1" }, { "subscriptionId": "sub-2" }] }""");

            var client = BuildClient(handler);
            var subs = await client.GetSubscriptionsAsync();

            Assert.Equal(2, subs.Length);
        }

        [Fact]
        public async Task GetSubscriptionsAsync_ServerError_ThrowsHttpRequestException()
        {
            var handler = new Mock<HttpMessageHandler>();
            SetupResponse(handler, HttpStatusCode.InternalServerError, "");

            var client = BuildClient(handler);

            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetSubscriptionsAsync());
        }

        [Fact]
        public async Task RunQueryAsync_NullResponse_ThrowsInvalidOperation()
        {
            var handler = new Mock<HttpMessageHandler>();
            SetupResponse(handler, HttpStatusCode.OK, "null");

            var client = BuildClient(handler);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.RunQueryAsync("sub-1", "some query"));
        }
    }
}