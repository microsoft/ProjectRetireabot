using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retirebot.Models;
using System.Text.RegularExpressions;

namespace Retirebot.Helpers
{
    public static partial class PreflightChecks
    {
        [GeneratedRegex(@"^[a-zA-Z0-9\-]+/[a-zA-Z0-9._\-]+$")]
        public static partial Regex RepoPattern();

        public static void StartPreflightChecks(FunctionsApplicationBuilder hostBuilder, IHost host, WorkItemBackend backend)
        {
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PreflightChecks");
            IConfiguration config = hostBuilder.Configuration;


            CheckTargetRepository(config);

            switch (backend)
            {
                case WorkItemBackend.GitHub:
                    CheckGitHubAuth(config, host, logger);
                    break;
            }
        }

        public static void CheckGitHubAuth(IConfiguration config, IHost host, ILogger logger)
        {
            GitHub.AuthModeService service = host.Services.GetRequiredService<GitHub.AuthModeService>();
            bool assignGHCP = config.GetSection(ConfigKeys.App.AssignGitHubCopilot).Get<bool>();

            switch (service.GetAuthMode())
            {
                case Models.GitHub.AuthMode.Hybrid:
                    logger.LogInformation("Using Hybrid GitHub authentication (PAT + App)");
                    break;
                case Models.GitHub.AuthMode.App:
                    if (assignGHCP)
                    {
                        logger.LogWarning("GitHub CoPilot assignment may fail on private repositories, consider using Hybrid mode");
                    }
                    break;
                case Models.GitHub.AuthMode.PAT:
                    logger.LogInformation("Using PAT GitHub authentication");
                    break;
                case Models.GitHub.AuthMode.None:
                default:
                    throw new InvalidOperationException("No supported GitHub credentials are available. Provide GitHub:PAT and/or GitHub:AppId");
            }
        }

        public static void CheckTargetRepository(IConfiguration config)
        {
            string? targetRepo = config.GetSection(ConfigKeys.App.TargetRepository).Get<string>();


            if (targetRepo == null || !RepoPattern().IsMatch(targetRepo))
            {
                throw new InvalidOperationException("App:TargetRepository is empty or not in the expected 'owner/repo' format");
            }
        }
    }
}