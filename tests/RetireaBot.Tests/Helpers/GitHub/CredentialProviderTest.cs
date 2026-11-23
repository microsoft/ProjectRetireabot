using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Helpers.GitHub;
using Moq;
using Octokit;

namespace Microsoft.RetireaBot.Tests.Helpers.GitHub
{
    public class CredentialProviderTest
    {
        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "Stale App/key configuration")]
        [InlineData(HttpStatusCode.NotFound, "GitHub:AppInstallId")]
        [InlineData(HttpStatusCode.Forbidden, "suspended")]
        public async Task RequestInstallationTokenAsync_ProvidesContextAndPreservesApiFailure(HttpStatusCode status, string guidance)
        {
            var apps = new Mock<IGitHubAppsClient>();
            var failure = new ApiException("GitHub rejected the request", status);
            apps.Setup(client => client.CreateInstallationToken(123)).ThrowsAsync(failure);
            var logger = new Mock<ILogger>();
            var keyId = new Uri("https://test.vault.azure.net/keys/app-key/version");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CredentialProvider.RequestInstallationTokenAsync(apps.Object, "test-app", 123, keyId, logger.Object));

            Assert.Same(failure, exception.InnerException);
            Assert.Contains($"HTTP {(int)status}", exception.Message);
            Assert.Contains("test-app", exception.Message);
            Assert.Contains("123", exception.Message);
            Assert.Contains(keyId.ToString(), exception.Message);
            Assert.Contains(guidance, exception.Message);
            Assert.Contains(failure.Message, exception.Message);
            logger.Verify(log => log.Log(
                LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                failure, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

        [Fact]
        public async Task RequestInstallationTokenAsync_ReturnsTokenWithoutLoggingIt()
        {
            var apps = new Mock<IGitHubAppsClient>();
            var token = new AccessToken("secret-test-token", DateTimeOffset.UtcNow.AddHours(1));
            apps.Setup(client => client.CreateInstallationToken(123)).ReturnsAsync(token);
            var logger = new Mock<ILogger>();

            var result = await CredentialProvider.RequestInstallationTokenAsync(
                apps.Object, "test-app", 123, new Uri("https://test.vault.azure.net/keys/app-key/version"), logger.Object);

            Assert.Same(token, result);
            foreach (var invocation in logger.Invocations.Where(call => call.Method.Name == "Log"))
            {
                Assert.DoesNotContain(token.Token, invocation.Arguments[2].ToString()!);
            }
        }
    }
}
