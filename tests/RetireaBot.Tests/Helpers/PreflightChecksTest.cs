using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.RetireaBot.Helpers;
using Microsoft.RetireaBot.Models;

namespace Microsoft.RetireaBot.Tests.Helpers
{
    public class PreflightChecksTest
    {
        private static IConfiguration BuildConfig(Dictionary<string, string?> settings) =>
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        [Theory]
        [InlineData("owner/repo")]
        [InlineData("my-org/my-repo.name")]
        [InlineData("microsoft/ProjectRetireaBot")]
        public void CheckTargetRepository_ValidGitHubRepository_DoesNotThrow(string repo)
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
        public void CheckTargetRepository_InvalidGitHubRepository_Throws(string repo)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.TargetRepository] = repo
            });

            Assert.Throws<InvalidOperationException>(() => PreflightChecks.CheckTargetRepository(config));
        }

        [Theory]
        [InlineData("RetireaBot")]
        [InlineData("My-Retirement-Repo")]
        [InlineData("Another_repository")]
        public void CheckADOProjectName_ValidADOProjectName_DoesNotThrow(string repo)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.TargetRepository] = repo
            });
            var exception = Record.Exception(() => PreflightChecks.CheckADOProjectName(config));
            Assert.Null(exception);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("web.config")]
        [InlineData("A+Totally+v@lid+repo")]
        [InlineData("owner/repo")]
        public void CheckADOProjectName_InvalidADOProjectName_Throws(string repo)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.TargetRepository] = repo
            });

            Assert.Throws<InvalidOperationException>(() => PreflightChecks.CheckADOProjectName(config));
        }


        [Fact]
        public void CheckGitHubAuth_NoCredentials_Throws()
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
                    services.AddSingleton<Microsoft.RetireaBot.Helpers.GitHub.AuthModeService>();
                    services.AddSingleton<Microsoft.RetireaBot.Helpers.GitHub.CredentialProvider>();
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("PreflightChecks");

            Assert.Throws<InvalidOperationException>(() => PreflightChecks.CheckGitHubAuth(config, host, logger));
        }

        [Theory]
        [InlineData("https://dev.azure.com/test-org")]
        [InlineData("https://testorg.visualstudio.com")]
        [InlineData("https://dev.azure.com/retireabot")]
        public void CheckADOOrganisationURL_ValidADOOrganisationURL_DoesNotThrow(string orgURL)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureDevOps.OrganisationUrl] = orgURL
            });
            var exception = Record.Exception(() => PreflightChecks.CheckADOOrganisationURL(config));
            Assert.Null(exception);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("dev.azure.com/test-org")]
        [InlineData("h t t p s : // t e s t o r g . v i s u a l s t u d i o . c o m")]
        [InlineData("https://INVALID@ORG.$$$dev.azure.com")]
        public void CheckADOOrganisationURL_InvalidADOOrganisationURL_Throws(string orgURL)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureDevOps.OrganisationUrl] = orgURL
            });

            Assert.Throws<InvalidOperationException>(() => PreflightChecks.CheckADOOrganisationURL(config));
        }

        [Theory]
        [InlineData("https://dev.azure.com/a")]                          // 1 char (minimum)
        [InlineData("https://dev.azure.com/ab")]                         // 2 chars
        [InlineData("https://dev.azure.com/abcdefghijklmnopqrstuvwxyz1234567890abcdefghijklmn")] // exactly 50 chars
        [InlineData("https://a.visualstudio.com")]                       // 1 char via subdomain
        [InlineData("https://abcdefghijklmnopqrstuvwxyz1234567890abcdefghijklmn.visualstudio.com")] // 50 chars via subdomain
        public void CheckADOOrganisationURL_BoundaryValidOrgName_DoesNotThrow(string orgURL)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureDevOps.OrganisationUrl] = orgURL
            });
            var exception = Record.Exception(() => PreflightChecks.CheckADOOrganisationURL(config));
            Assert.Null(exception);
        }

        [Theory]
        [InlineData("https://dev.azure.com/abcdefghijklmnopqrstuvwxyz1234567890abcdefghijklmno")] // 51 chars - too long
        [InlineData("https://dev.azure.com/-startswith-hyphen")]         // starts with hyphen
        [InlineData("https://dev.azure.com/endswith-hyphen-")]           // ends with hyphen
        [InlineData("https://dev.azure.com/-")]                          // single hyphen
        [InlineData("https://abcdefghijklmnopqrstuvwxyz1234567890abcdefghijklmno.visualstudio.com")] // 51 chars subdomain
        [InlineData("https://-invalid.visualstudio.com")]                // starts with hyphen subdomain
        public void CheckADOOrganisationURL_BoundaryInvalidOrgName_Throws(string orgURL)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureDevOps.OrganisationUrl] = orgURL
            });

            Assert.Throws<InvalidOperationException>(() => PreflightChecks.CheckADOOrganisationURL(config));
        }

        [Theory]
        [InlineData([null, null, null, null])]
        [InlineData(["azure-advisor", "tracking", "advisor-", "advisor-type-"])]
        [InlineData(["fabkrim-project", "parent", "fabkrim-", "parent-type-"])]
        [InlineData(["project8", "tracking8", "project8-", "project8-type-"])]
        [InlineData("my.label.with.dots", "parent-label", "prefix-", "type-prefix-")]
        [InlineData("UPPERCASE", "TRACKING", "UPPER-", "TYPE-")]
        [InlineData("a", "b", "c-", "d-")]
        [InlineData("label with spaces", "tracking item", "spaced prefix ", "type prefix ")]
        [InlineData("special!@#$%^&*()", "tracking", "prefix-", "type-")]
        public void CheckADOLables_ValidLabels_DoesNotThrows(string advisoryLabel, string advisoryParentLabel, string advisoryLabelPrefix, string parentLabelPrefix)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.AdvisoryLabel] = advisoryLabel,
                [ConfigKeys.App.AdvisoryParentLabel] = advisoryParentLabel,
                [ConfigKeys.App.AdvisoryLabelPrefix] = advisoryLabelPrefix,
                [ConfigKeys.App.ParentLabelPrefix] = parentLabelPrefix
            });

            var exception = Record.Exception(() => PreflightChecks.CheckADOLabels(config, Mock.Of<ILogger<PreflightChecksTest>>()));
            Assert.Null(exception);
        }

        [Theory]
        [InlineData("azure;advisor", "tracking", "advisor-", "advisor-type-")]
        [InlineData("azure-advisor", "track,ing", "advisor-", "advisor-type-")]
        [InlineData("azure-advisor", "tracking", "advisor,prefix-", "advisor-type-")]
        [InlineData("azure-advisor", "tracking", "advisor-", "type;prefix-")]
        [InlineData("label\nwith\nnewlines", "tracking", "advisor-", "advisor-type-")]
        [InlineData("azure-advisor", "tracking\r\n", "advisor-", "advisor-type-")]
        [InlineData("azure-advisor", "tracking", "advisor-", "prefix\twith\ttabs")]
        public void CheckADOLables_InvalidLabels_DoesNotThrows(string advisoryLabel, string advisoryParentLabel, string advisoryLabelPrefix, string parentLabelPrefix)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.AdvisoryLabel] = advisoryLabel,
                [ConfigKeys.App.AdvisoryParentLabel] = advisoryParentLabel,
                [ConfigKeys.App.AdvisoryLabelPrefix] = advisoryLabelPrefix,
                [ConfigKeys.App.ParentLabelPrefix] = parentLabelPrefix
            });

            Assert.Throws<InvalidOperationException>(() => PreflightChecks.CheckADOLabels(config, Mock.Of<ILogger<PreflightChecksTest>>()));
        }

        [Fact]
        public void CheckADOLabels_LabelAt300Chars_LogsWarning()
        {
            string longLabel = new string('a', 301);
            var mockLogger = new Mock<ILogger<PreflightChecksTest>>();

            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.AdvisoryLabel] = longLabel,
                [ConfigKeys.App.AdvisoryParentLabel] = "tracking",
                [ConfigKeys.App.AdvisoryLabelPrefix] = "advisor-",
                [ConfigKeys.App.ParentLabelPrefix] = "advisor-type-"
            });

            // Should not throw, but should warn
            var exception = Record.Exception(() => PreflightChecks.CheckADOLabels(config, mockLogger.Object));
            Assert.Null(exception);
        }

        [Fact]
        public void CheckADOLabels_LabelOver400Chars_Throws()
        {
            string tooLongLabel = new string('a', 401);

            var config = BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.App.AdvisoryLabel] = tooLongLabel,
                [ConfigKeys.App.AdvisoryParentLabel] = "tracking",
                [ConfigKeys.App.AdvisoryLabelPrefix] = "advisor-",
                [ConfigKeys.App.ParentLabelPrefix] = "advisor-type-"
            });

            Assert.Throws<InvalidOperationException>(() => PreflightChecks.CheckADOLabels(config, Mock.Of<ILogger<PreflightChecksTest>>()));
        }
    }
}