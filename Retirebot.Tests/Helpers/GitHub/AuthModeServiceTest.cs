using Microsoft.Extensions.Configuration;
using Microsoft.RetireaBot.Helpers.GitHub;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.GitHub;

namespace Microsoft.RetireaBot.Tests.Helpers.GitHub
{
    public class AuthModeServiceTest
    {
        private static IConfiguration BuildConfig(Dictionary<string, string?> settings) =>
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        [Fact]
        public void GetAuthMode_NoCredentials_ReturnsNone()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>()));
            Assert.Equal(AuthMode.None, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_PATOnly_ReturnsPAT()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.GitHub.PAT] = "ghp_test123"
            }));

            Assert.Equal(AuthMode.PAT, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_AppOnly_ReturnsApp()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.GitHub.AppId] = "12345",
                [ConfigKeys.GitHub.AppPrivateKeyId] = "key-id",
                [ConfigKeys.GitHub.AppInstallId] = "67890",
            }));

            Assert.Equal(AuthMode.App, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_BothPATAndApp_ReturnsHybrid()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.GitHub.PAT] = "ghp_test123",
                [ConfigKeys.GitHub.AppId] = "12345",
                [ConfigKeys.GitHub.AppPrivateKeyId] = "key-id",
                [ConfigKeys.GitHub.AppInstallId] = "67890",
            }));

            Assert.Equal(AuthMode.Hybrid, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_PartialAppConfig_ReturnsNone()
        {
            // Missing AppInstallId — should not count as App auth
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.GitHub.AppId] = "12345",
                [ConfigKeys.GitHub.AppPrivateKeyId] = "key-id",
            }));

            Assert.Equal(AuthMode.None, service.GetAuthMode());
        }
    }
}