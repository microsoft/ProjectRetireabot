using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;
using Retirebot.Helpers;
using Retirebot.Models;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Retirebot.Functions
{
    public class GetRetirements
    {
        private readonly ILogger _logger;
        private readonly ManagementClient _managementClient;
        private readonly GitHubClient _ghClient;
        private readonly string _advisoryQuery;

        private readonly string _targetRepository;
        private readonly WorkItemScope _workItemScope;
        private readonly List<AzureRepositoryMap> _rgRepoMapping;

        private readonly bool _assignGHCP;
        private readonly bool _enableHTTPEndpoint;

        private readonly bool _createParentIssues;

        private readonly string _baseQuery = "advisorresources | where properties.extendedProperties.recommendationSubCategory == \"ServiceUpgradeAndRetirement\" | where tostring(properties.category) has \"HighAvailability\" | extend resourceId = tostring(properties.resourceMetadata.resourceId) | project id, name, type, subscriptionId, resourceGroup, location, resourceId, ServiceID = tostring(properties.recommendationTypeId), impact = tostring(properties.impact), category = tostring(properties.category), impactedField = tostring(properties.impactedField), impactedValue = tostring(properties.impactedValue), lastUpdated = tostring(properties.lastUpdated), retirementDate = tostring(properties.extendedProperties.retirementDate), retirementFeatureName = tostring(properties.extendedProperties.retirementFeatureName), maturityLevel = tostring(properties.extendedProperties.maturityLevel), recommendationOfferingId = tostring(properties.extendedProperties.recommendationOfferingId), shortDescriptionProblem = tostring(properties.shortDescription.problem), shortDescriptionSolution = tostring(properties.shortDescription.solution)";

        private readonly IConfiguration _config;

        public GetRetirements(ILoggerFactory loggerFactory, IConfiguration config, ManagementClient client, GitHubClient ghClient)
        {
            _logger = loggerFactory.CreateLogger<GetRetirements>();
            _managementClient = client;
            _ghClient = ghClient;
            _config = config;

            _targetRepository = _config.GetSection("GitHub:TargetRepository").Get<string>() ?? throw new InvalidOperationException("GitHub:TargetRepository is not configured.");
            _workItemScope = Enum.Parse<WorkItemScope>(_config.GetSection("Azure:WorkItemScope").Get<string>() ?? "monolithic", true);

            _createParentIssues = config.GetSection("Azure:CreateParentIssues").Get<bool>();

            string? mappingJson = _config.GetSection("Azure:TargetResourceGroupMapping").Get<string>();
            _rgRepoMapping = !string.IsNullOrEmpty(mappingJson)
                ? JsonSerializer.Deserialize<List<AzureRepositoryMap>>(mappingJson) ?? []
                : [];

            _assignGHCP = _config.GetSection("App:AssignGitHubCopilot").Get<bool>();
            _enableHTTPEndpoint = _config.GetSection("App:EnableHTTPEndpoint").Get<bool>();

            string? rg = _config.GetSection("Azure:TargetResourceGroup").Get<string>();
            _advisoryQuery = rg != null ? $"{_baseQuery} | where resourceGroup has \"{rg}\"" : _baseQuery;
        }

        [Function("GetRetirements")]
        public async Task RunTimer([TimerTrigger("%App:TimerTrigger%")] TimerInfo timerInfo)
        {
            _logger.LogInformation("Retrieving Retirements via Timer");
            await GetRetirementsASync();
            _logger.LogInformation("Next timer schedule = {NextSchedule}", timerInfo.ScheduleStatus?.Next);
        }

        [Function("GetRetirementsManual")]
        public async Task<HttpResponseData> RunHttp([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            if (!_enableHTTPEndpoint)
            {
                _logger.LogDebug("Manual Endpoint hit when App:EnableHTTPEndpoint is disabled");
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            _logger.LogInformation("Retrieving Retirements via HTTP Manual trigger");

            try
            {
                await GetRetirementsASync();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteStringAsync("Completed successfully.");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError("Caught exception whilst handling request.\n{Exception}", ex);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteStringAsync("Error whilst completing action. Please check App Insights for more information.");
                return response;
            }
        }

        private string GetRepositoryForAdvisory(Advisory advisory)
        {
            if (_workItemScope != WorkItemScope.PerResourceGroup)
            {
                return _targetRepository;
            }

            string resourceGroup = advisory.GetResourceGroupName();

            var mapping = _rgRepoMapping.FirstOrDefault(m =>
                m.Type == AzureContainerType.ResourceGroup
                && string.Equals(m.Name, resourceGroup, StringComparison.OrdinalIgnoreCase));

            return mapping?.Repository ?? _targetRepository;
        }

        public async Task GetRetirementsASync()
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
                QueryResult<ARGRetirementData> data = await _managementClient.RunQueryAsync(sub, _advisoryQuery);

                for (int i = 0; i < data.Length; i++)
                {
                    advisories.Add(data.Data[i].ToAdvisory());
                }
            }

            Dictionary<string, List<Advisory>> advisoriesByRepo = advisories.GroupBy(GetRepositoryForAdvisory).ToDictionary(g => g.Key, g => g.ToList());

            // RecommendationTypeId -> (repo -> list of child issues)
            Dictionary<string, Dictionary<string, List<Issue>>> childIssuesByType = [];

            // RecommendationTypeId -> representative advisory (for title/description)
            Dictionary<string, Advisory> representativeByType = [];

            foreach (var (repo, repoAdvisories) in advisoriesByRepo)
            {
                _logger.LogInformation("Processing {Count} advisories for repository {Repo}", repoAdvisories.Count, repo);

                Dictionary<string, Issue> existingIssues = await GitHubHelper.FindExistingIssuesByLabelsAsync(_logger, _ghClient, repoAdvisories, repo);
                List<Advisory> advisoriesToCreate = repoAdvisories.Where(a => !existingIssues.ContainsKey(a.Name)).ToList();

                _logger.LogInformation("Found {ExistingCount} existing issues, creating {NewCount} new issues in {Repo}",
                    existingIssues.Count, advisoriesToCreate.Count, repo);

                List<Issue> createdIssues = await GitHubHelper.CreateIssuesBatch(_logger, _ghClient, advisoriesToCreate, repo, _assignGHCP);

                if (_createParentIssues)
                {
                    // Map created issues back to their advisory by index
                    var createdPairs = advisoriesToCreate.Zip(createdIssues, (advisory, issue) => (advisory, issue));

                    // Map existing issues back to their advisory by name
                    var existingPairs = existingIssues.Select(kvp =>
                    {
                        var advisory = repoAdvisories.First(a => a.Name == kvp.Key);
                        return (advisory, issue: kvp.Value);
                    });

                    foreach (var (advisory, issue) in createdPairs.Concat(existingPairs))
                    {
                        string typeId = advisory.Properties.RecommendationTypeId;

                        if (!childIssuesByType.ContainsKey(typeId))
                        {
                            childIssuesByType[typeId] = [];
                            representativeByType[typeId] = advisory;
                        }

                        if (!childIssuesByType[typeId].ContainsKey(repo))
                        {
                            childIssuesByType[typeId][repo] = [];
                        }

                        childIssuesByType[typeId][repo].Add(issue);
                    }
                }
            }

            if (_createParentIssues)
            {
                foreach (var (typeId, childIssuesByRepo) in childIssuesByType)
                {
                    await GitHubHelper.FindOrCreateParentIssueAsync(
                        _logger,
                        _ghClient,
                        typeId,
                        representativeByType[typeId],
                        childIssuesByRepo,
                        _targetRepository);
                }
            }

            sw.Stop();
            _logger.LogInformation("Function ran. Approximately took {ElapsedSeconds} second(s)", sw.Elapsed.TotalSeconds);
        }
    }
}
