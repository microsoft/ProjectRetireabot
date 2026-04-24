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

        [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9-]{0,48}[a-zA-Z0-9]$|^[a-zA-Z0-9]$")]
        public static partial Regex ADOOrganisationNamePattern();

        [GeneratedRegex(@"^(?!_)(?!\.)(?!(?:App_Browsers|App_code|App_Data|App_GlobalResources|App_LocalResources|App_Themes|App_WebResources|bin|web\.config)$)[^\\/:\*\?""'<>;#\$\{\},\+=\[\]\|\p{Cc}\p{Cs}]{1,64}(?<!\.)$")]
        public static partial Regex ADOProjectNamePattern();

        [GeneratedRegex(@"[,;\p{Cc}\p{Cf}]")]
        private static partial Regex ADOInvalidTagPattern();

        public static void StartPreflightChecks(IConfiguration config, IHost host, WorkItemBackend backend)
        {
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PreflightChecks");

            switch (backend)
            {
                case WorkItemBackend.AzureDevOps:
                    CheckADOOrganisationURL(config);
                    CheckADOProjectName(config);
                    CheckADOLabels(config, logger);
                    break;
                case WorkItemBackend.GitHub:
                    CheckGitHubAuth(config, host, logger);
                    CheckTargetRepository(config);
                    break;
            }
        }

        public static void CheckGitHubAuth(IConfiguration config, IHost host, ILogger logger)
        {
            GitHub.AuthModeService service = host.Services.GetRequiredService<GitHub.AuthModeService>();
            bool assignCopilot = config.GetSection(ConfigKeys.App.AssignGitHubCopilot).Get<bool?>() ?? false;

            switch (service.GetAuthMode())
            {
                case Models.GitHub.AuthMode.Hybrid:
                    logger.LogInformation("Using Hybrid GitHub authentication (PAT + App)");
                    break;
                case Models.GitHub.AuthMode.App:
                    if (assignCopilot)
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

        public static void CheckADOOrganisationURL(IConfiguration config)
        {
            string? orgUrl = config.GetSection(ConfigKeys.AzureDevOps.OrganisationUrl).Get<string>();

            if (string.IsNullOrEmpty(orgUrl))
            {
                throw new InvalidOperationException("AzureDevOps:OrganisationUrl is not configured.");
            }

            if (!Uri.TryCreate(orgUrl, UriKind.Absolute, out Uri? uri) || (uri.Scheme != "https"))
            {
                throw new InvalidOperationException("AzureDevOps:OrganisationUrl must be a valid HTTPS URL.");
            }

            string orgName = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault() ?? "";

            if (!ADOOrganisationNamePattern().IsMatch(orgName))
            {
                throw new InvalidOperationException($"AzureDevOps organisation name '{orgName}' is invalid. Must start/end with a letter or number, contain only letters, numbers, or hyphens, and be under 50 characters.");
            }
        }

        public static void CheckADOProjectName(IConfiguration config)
        {
            string? targetProject = config.GetSection(ConfigKeys.App.TargetRepository).Get<string>();

            if (string.IsNullOrEmpty(targetProject))
            {
                throw new InvalidOperationException("App:TargetRepository is not configured.");
            }

            if (!ADOProjectNamePattern().IsMatch(targetProject))
            {
                throw new InvalidOperationException($"App:TargetRepository '{targetProject}' is not a valid Azure DevOps project name.");
            }
        }
    
        public static void CheckADOLabels(IConfiguration config, ILogger logger)
        {
            Dictionary<string, string?> labelPairs = new Dictionary<string, string?>() {
                { ConfigKeys.App.AdvisoryLabel, config.GetSection(ConfigKeys.App.AdvisoryLabel).Get<string>() },
                { ConfigKeys.App.AdvisoryParentLabel,  config.GetSection(ConfigKeys.App.AdvisoryParentLabel).Get<string>() },
                { ConfigKeys.App.AdvisoryLabelPrefix,config.GetSection(ConfigKeys.App.AdvisoryLabelPrefix).Get <string>() },
                { ConfigKeys.App.ParentLabelPrefix, config.GetSection(ConfigKeys.App.ParentLabelPrefix).Get<string>() }
            };
            
            foreach (var setting in labelPairs)
            {
                if (setting.Value == null) continue;
                
                if (ADOInvalidTagPattern().IsMatch(setting.Value))
                {
                    throw new InvalidOperationException($"{setting.Key} is equal to '{setting.Value}' which is not a valid Azure DevOps tag. Please remove any invalid characters.");
                }

                if (setting.Value.Length > 300)
                {
                    logger.LogWarning("{labelSettingName} is {labelSettingLength} characters long which is near the 400 character limit imposed by Azure DevOps. This may cause issues with Work Item duplication checking and general reliabilty.", setting.Key, setting.Value.Length);
                }
            }
         }
    }
}