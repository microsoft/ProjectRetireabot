using Retirebot.Helpers;
using Retirebot.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Octokit;
using System.Diagnostics;

namespace Retirebot.Functions
{
    public class GetRetirements
    {
        private readonly ILogger _logger;
        private readonly ManagementClient _managementClient;
        private readonly GitHubClient _ghClient;

        public GetRetirements(ILoggerFactory loggerFactory, ManagementClient client, GitHubClient ghClient)
        {
            _logger = loggerFactory.CreateLogger<GetRetirements>();
            _managementClient = client;
            _ghClient = ghClient;
        }

        // Runs every Monday at 00:00 GMT
        [Function("GetRetirements")]
        public async Task Run([TimerTrigger("0 0 0 * * 1")] TimerInfo timerInfo)
        {
            Stopwatch sw = Stopwatch.StartNew();
            _logger.LogInformation("Running function at {CurrentTime}", DateTime.UtcNow);

            string[] subs = await _managementClient.GetSubscriptionsAsync();
            if (subs is null || subs.Length == 0)
            {
                _logger.LogWarning("No subscriptions returned; aborting.");
                return;
            }

            List<Advisory> advisories = new List<Advisory>();

            foreach (string sub in subs)
            {
                QueryResult<ARGRetirementData> data = await _managementClient.RunQueryAsync(sub, "advisorresources | where properties.extendedProperties.recommendationSubCategory == \"ServiceUpgradeAndRetirement\" | where tostring(properties.category) has \"HighAvailability\" | extend resourceId = tostring(properties.resourceMetadata.resourceId) | project id, subscriptionId, resourceGroup, location, resourceId, ServiceID = tostring(properties.recommendationTypeId)");

                for (int i = 0; i < data.Length; i++)
                {
                    string advisoryId = data.Data[i].Id;
                    try
                    {
                        advisories.Add(await _managementClient.GetAdvisoryAsync(advisoryId));
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogWarning(ex, "Failed to get advisory for resource {AdvisoryId}", advisoryId);
                    }
                }
            }

            Dictionary<string, Issue> existingIssues = await GitHubHelper.FindExistingIssuesByLabelsAsync(_logger, _ghClient, advisories);
            List<Advisory> advisoriesToCreate = advisories.Where(a => !existingIssues.ContainsKey(a.Name)).ToList();

            _logger.LogInformation("Found {ExistingCount} existing issues, creating {NewCount} new issues", existingIssues.Count, advisoriesToCreate.Count);

            var createdIssues = await GitHubHelper.CreateIssuesBatch(_logger, _ghClient, advisoriesToCreate);

            sw.Stop();
            _logger.LogInformation("Function ran. Approximately took {ElapsedSeconds} second(s)", sw.Elapsed.TotalSeconds);
            _logger.LogInformation("Next timer schedule = {NextSchedule}", timerInfo.ScheduleStatus?.Next);
        }
    }
}
