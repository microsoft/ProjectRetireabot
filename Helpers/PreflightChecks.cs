using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Retirebot.Helpers
{
    public static partial class PreflightChecks
    {
        [GeneratedRegex(@"^[a-zA-Z0-9\-]+/[a-zA-Z0-9._\-]+$")]
        public static partial Regex RepoPattern();

        public static void StartPreflightChecks(IConfiguration config, ILoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger("PreflightChecks");

            CheckTargetRepository(config);
            CheckGitHubAuth(config, logger);
        }

        public static void CheckGitHubAuth(IConfiguration config, ILogger logger)
        {
            string? appId = config.GetSection("GitHub:AppId").Get<string>();
            string? appPrivateKeyId = config.GetSection("GitHub:AppPrivateKeyId").Get<string>();
            long? appInstallId = config.GetSection("GitHub:AppInstallId").Get<long?>();
            bool assignGHCP = config.GetSection("App:AssignGitHubCopilot").Get<bool>();

            string? pat = config.GetSection("GitHub:PAT").Get<string>();

            GitHubAuthMode AuthMode = (!string.IsNullOrEmpty(pat) ? GitHubAuthMode.PAT : GitHubAuthMode.None) | (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(appPrivateKeyId) && appInstallId != null ? GitHubAuthMode.App : GitHubAuthMode.None);

            switch (AuthMode)
            {
                case GitHubAuthMode.Hybrid:
                    logger.LogInformation("Using Hybrid GitHub authentication (PAT + App)");
                    break;
                case GitHubAuthMode.App:
                    if (assignGHCP)
                    {
                        logger.LogWarning("GitHub CoPilot assignment may fail on private repositories, consider using Hybrid mode");
                    }
                    break;
                case GitHubAuthMode.PAT:
                    logger.LogInformation("Using PAT GitHub authentication");
                    break;
                case GitHubAuthMode.None:
                default:
                    throw new InvalidOperationException("No supported GitHub credentials are available. Provide GitHub:PAT and/or GitHub:AppId");
            }
        }

        public static void CheckTargetRepository(IConfiguration config)
        {
            string? targetRepo = config.GetSection("GitHub:TargetRepository").Get<string>();


            if (targetRepo == null || !RepoPattern().IsMatch(targetRepo))
            {
                throw new InvalidOperationException("GitHub:TargetRepository is empty or not in the expected 'owner/repo' format");
            }
        }
    }
}
