using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Octokit;
using Retirebot.Models;

namespace Retirebot
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

        [Function("GetRetirements")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] TimerInfo myTimer)
        {
            //// Use _armClient to run Resource Graph queries
            //// Example: var response = await _armClient.GetTenantResourceGraphAsync(content);

            //_logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            
            //if (myTimer.ScheduleStatus is not null)
            //{
            //    _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            //}

            var subs = await _managementClient.GetSubscriptionsAsync();
            QueryResult<ARGRetirementData> data = await _managementClient.RunQueryAsync(subs[0], "advisorresources | where properties.extendedProperties.recommendationSubCategory == \"ServiceUpgradeAndRetirement\" | where tostring(properties.category) has \"HighAvailability\" | extend resourceId = tostring(properties.resourceMetadata.resourceId) | project id, subscriptionId, resourceGroup, location, resourceId, ServiceID = tostring(properties.recommendationTypeId)");

            List<Advisory> advisories = new List<Advisory>();

            for (int i = 0; i < data.Length; i++)
            {
                string advisoryId = data.Data[i].Id;
                try
                {
                    advisories.Add(await _managementClient.GetAdvisoryAsync(advisoryId));
                } catch(HttpRequestException ex)
                {
                    _logger.LogWarning("Failed to get advisory for resource {AdvisoryId}", advisoryId);
                    _logger.LogWarning($"{ex}");
                }
            }

            Dictionary<string, Issue> existingIssues = await GitHubHelper.FindExistingIssuesByLabelsAsync(_logger, _ghClient, advisories);
            List<Advisory> advisoriesToCreate = advisories.Where(a => !existingIssues.ContainsKey(a.Name)).ToList();

            _logger.LogInformation("Found {ExistingCount} existing issues, creating {NewCount} new issues", existingIssues.Count, advisoriesToCreate.Count);

            

            return new OkObjectResult(advisories);
        }
    }
}
