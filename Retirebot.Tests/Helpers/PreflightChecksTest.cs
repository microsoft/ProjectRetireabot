using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retirebot.Helpers;
using Retirebot.Models;

namespace Retirebot.Tests.Helpers
{
    public class PreflightChecksTest
    {
        private static IConfiguration BuildConfig(Dictionary<string, string?> settings)
        {
            return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        }

        [Theory]
        [InlineData("owner/repo")]
        [InlineData("my-org/my-repo.name")]
        [InlineData("ZanyLeonic/RetireBot")]
        public async Task CheckTargetRepository_ValidRepo_DoesNotThrow(string repo)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.TargetRepository] = repo
            });
            var exception = Record.Exception(() => PreflightChecks.CheckTargetRepository(config));
            Assert.Null(exception);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("noslash")]
        [InlineData("owner/repo/extra")]
        [InlineData("owner/ repo")]
        public async Task PreflightChecks_TestInvalidRepository(string repo)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.TargetRepository] = repo
            });

            Assert.Throws<InvalidOperationException>(() => PreflightChecks.CheckTargetRepository(config));
        }

        [Fact]
        public async Task CheckGitHubAuth_NoCredentials_Throws()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.AssignGitHubCopilot] = "false"
            });

            // Build a minimal IHost with the required services
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(config);
                    services.AddSingleton<Retirebot.Helpers.GitHub.AuthModeService>();
                    services.AddSingleton<Retirebot.Helpers.GitHub.CredentialProvider>();
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("PreflightChecks");

            Assert.Throws<InvalidOperationException>(() => PreflightChecks.CheckGitHubAuth(config, host, logger));
        }
    }
}