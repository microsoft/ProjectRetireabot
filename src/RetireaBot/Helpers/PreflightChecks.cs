using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Sinks.PowerBI;
using System.Collections;
using System.Text.RegularExpressions;

namespace Microsoft.RetireaBot.Helpers
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

        public static void StartPreflightChecks(IConfiguration config, IHost host, List<WorkItemBackend> workItems, List<DataSinkBackend> dataSinks)
        {
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PreflightChecks");
            IVendorSettingsProvider vendorSettings = host.Services.GetRequiredService<IVendorSettingsProvider>();

            CheckOutputConfiguration(config, workItems, dataSinks);

            foreach (WorkItemBackend backend in workItems)
            {
                IVendorSettings vendor = vendorSettings.For(backend);

                switch (backend)
                {
                    case WorkItemBackend.AzureDevOps:
                        CheckADOOrganisationURL(config);
                        CheckADOProjectName(vendor);
                        CheckADOLabels(config, logger);
                        break;
                    case WorkItemBackend.GitHub:
                        CheckGitHubAuth(config, host, logger);
                        CheckTargetRepository(vendor);
                        break;
                }
            }

            foreach (DataSinkBackend backend in dataSinks)
            {
                switch (backend)
                {
                    case DataSinkBackend.PowerBI:
                        CheckPowerBIConfiguration(config);
                        break;
                }
            }
        }

        public static void CheckOutputConfiguration(IConfiguration config, List<WorkItemBackend> workItems, List<DataSinkBackend> dataSinks)
        {
            bool httpOutput = config.GetSection(ConfigKeys.App.HTTPEndpointEnable).Get<bool>() && config.GetSection(ConfigKeys.App.HTTPEndpointOutput).Get<bool>();

            if (httpOutput && workItems.Count == 0 && dataSinks.Count == 0)
            {
                throw new InvalidOperationException("No outputs are configured for RetireaBot. HTTPEndpointOutput enabled, at least one WorkItem or DataSink backend is required for normal operation.");
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

        public static void CheckTargetRepository(IVendorSettings vendor)
        {
            if (string.IsNullOrEmpty(vendor.TargetRepository) || !RepoPattern().IsMatch(vendor.TargetRepository))
            {
                throw new InvalidOperationException($"{vendor.Backend}:TargetRepository is empty or not in the expected 'owner/repo' format");
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

            string orgName;

            if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
            {
                // Legacy format: https://{org}.visualstudio.com
                orgName = uri.Host.Split('.')[0];
            }
            else if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                // Modern format: https://dev.azure.com/{org}
                orgName = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault() ?? "";
            }
            else
            {
                throw new InvalidOperationException("AzureDevOps:OrganisationUrl must be a dev.azure.com or visualstudio.com URL.");
            }

            if (!ADOOrganisationNamePattern().IsMatch(orgName))
            {
                throw new InvalidOperationException($"AzureDevOps organisation name '{orgName}' is invalid. Must start/end with a letter or number, contain only letters, numbers, or hyphens, and be under 50 characters.");
            }
        }

        public static void CheckADOProjectName(IVendorSettings vendor)
        {
            if (string.IsNullOrEmpty(vendor.TargetRepository))
            {
                throw new InvalidOperationException("AzureDevOps:TargetRepository is not configured.");
            }

            if (!ADOProjectNamePattern().IsMatch(vendor.TargetRepository))
            {
                throw new InvalidOperationException($"AzureDevOps:TargetRepository '{vendor.TargetRepository}' is not a valid Azure DevOps project name.");
            }
        }

        public static void CheckADOLabels(IConfiguration config, ILogger logger)
        {
            Dictionary<string, string?> labelPairs = new Dictionary<string, string?>() {
                { ConfigKeys.AzureDevOps.AdvisoryLabel, config.GetSection(ConfigKeys.AzureDevOps.AdvisoryLabel).Get<string>() },
                { ConfigKeys.AzureDevOps.AdvisoryParentLabel,  config.GetSection(ConfigKeys.AzureDevOps.AdvisoryParentLabel).Get<string>() },
                { ConfigKeys.AzureDevOps.AdvisoryLabelPrefix, config.GetSection(ConfigKeys.AzureDevOps.AdvisoryLabelPrefix).Get<string>() },
                { ConfigKeys.AzureDevOps.AdvisoryParentLabelPrefix, config.GetSection(ConfigKeys.AzureDevOps.AdvisoryParentLabelPrefix).Get<string>() }
            };

            foreach (var setting in labelPairs)
            {
                if (setting.Value == null) continue;

                if (setting.Value.Length > 400)
                {
                    throw new InvalidOperationException($"{setting.Key} is over the Azure DevOps tag limit of 400 characters. Please trim or reduce the amount of characters for this tag.");
                }

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

        public static void CheckPowerBIConfiguration(IConfiguration config)
        {
            string? datasetId = config.GetSection(ConfigKeys.PowerBI.DatasetId).Get<string>();
            if (string.IsNullOrWhiteSpace(datasetId))
                throw new InvalidOperationException("PowerBI:DatasetId is not configured.");

            string? tableName = config.GetSection(ConfigKeys.PowerBI.TableName).Get<string>();
            if (string.IsNullOrWhiteSpace(tableName))
                throw new InvalidOperationException("PowerBI:TableName is not configured.");

            string? writeMode = config.GetSection(ConfigKeys.PowerBI.WriteMode).Get<string>();
            if (writeMode != null && !Enum.TryParse<WriteMode>(writeMode, ignoreCase: true, out _))
                throw new InvalidOperationException(
                    $"PowerBI:WriteMode '{writeMode}' is not valid. Expected: Append, Snapshot.");
        }
    }
}