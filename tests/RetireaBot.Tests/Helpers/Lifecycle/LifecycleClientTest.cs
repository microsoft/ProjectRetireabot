using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.RetireaBot.Helpers.Lifecycle;
using Microsoft.RetireaBot.Models.Lifecycle;
using Moq;
using Moq.Protected;

namespace Microsoft.RetireaBot.Tests.Helpers.Lifecycle
{
    public class LifecycleClientTest
    {
        private const string PostgreSqlSampleHtml = """
            | PostgreSQL Version | What's New | Azure Standard Support Start Date | Azure Standard Support End Date |
                | --- | --- | --- | --- |
                | [PostgreSQL 18](https://www.postgresql.org/about/press/) | [Release notes](https://www.postgresql.org/docs/18/release-18.html) | 25-Sep-2025| 14-Nov-2030 |
            """;

        private static (LifecycleClient client, Mock<HttpMessageHandler> handler) BuildClient(HttpStatusCode status, string body)
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = status,
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html"),
                });

            var http = new HttpClient(handler.Object);
            return (new LifecycleClient(http, NullLoggerFactory.Instance), handler);
        }

        [Fact]
        public async Task GetEntriesAsync_PostgreSql_ParsesAndCaches()
        {
            var (client, handler) = BuildClient(HttpStatusCode.OK, PostgreSqlSampleHtml);

            var first = await client.GetProductEntriesASync(LifecycleProduct.AzurePostgreSQLFlexible, "https://example/pg");
            var second = await client.GetProductEntriesASync(LifecycleProduct.AzurePostgreSQLFlexible, "https://example/pg");

            Assert.Single(first);
            Assert.Equal("18", first[0].Version);
            Assert.Equal(new DateTime(2030, 11, 14), first[0].EndOfLife);
            Assert.Same(first, second); // cached

            // Second call should not have triggered a second HTTP request.
            handler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task GetEntriesAsync_HttpError_ReturnsEmptyAndDoesNotThrow()
        {
            var (client, _) = BuildClient(HttpStatusCode.InternalServerError, "boom");
            var entries = await client.GetProductEntriesASync(LifecycleProduct.AksKubernetes, "https://example/aks");
            Assert.Empty(entries);
        }
    }
}