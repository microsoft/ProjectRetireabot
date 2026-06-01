using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Helpers;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.HTTP;
using Microsoft.RetireaBot.Models.Azure;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.RetireaBot.Helpers.Lifecycle;
using Microsoft.RetireaBot.Models.Lifecycle;

namespace Microsoft.RetireaBot.Functions
{
    public class GetRetirements
    {
        private readonly ILogger _logger;
        private readonly LifecycleClient _lifecycleClient;
        private readonly Helpers.Azure.ManagementClient _managementClient;
        private readonly string _advisoryQuery;

        private readonly bool _createParentWorkItems;
        private readonly bool _createChildWorkItems;

        private readonly string _targetRepository;
        private readonly WorkItemScope _workItemScope;
        private readonly List<AzureRepositoryMap> _rgRepoMapping;

        private readonly bool _assignCopilot;

        private readonly bool _httpEndpointEnable;
        private readonly bool _httpEndpointOutput;
        private readonly bool _httpEndpointWhatIf;

        private readonly bool _lifecycleSignalsEnable;
        private readonly TimeSpan _lifecycleWarningWindow;

        private readonly string _aksResourceQuery = "resources | where type =~ 'microsoft.containerservice/managedclusters' | project id, name, type, subscriptionId, resourceGroup, location, version = tostring(properties.kubernetesVersion)";
        private readonly string _postgreSqlResourceQuery = "resources | where type =~ 'microsoft.dbforpostgresql/flexibleservers' | project id, name, type, subscriptionId, resourceGroup, location, version = tostring(properties.version)";
        private readonly string _baseQuery = "advisorresources | where properties.extendedProperties.recommendationSubCategory == \"ServiceUpgradeAndRetirement\" | where tostring(properties.category) has \"HighAvailability\" | extend resourceId = tostring(properties.resourceMetadata.resourceId) | project id, name, type, subscriptionId, resourceGroup, location, resourceId, ServiceID = tostring(properties.recommendationTypeId), impact = tostring(properties.impact), category = tostring(properties.category), impactedField = tostring(properties.impactedField), impactedValue = tostring(properties.impactedValue), lastUpdated = tostring(properties.lastUpdated), retirementDate = tostring(properties.extendedProperties.retirementDate), retirementFeatureName = tostring(properties.extendedProperties.retirementFeatureName), maturityLevel = tostring(properties.extendedProperties.maturityLevel), recommendationOfferingId = tostring(properties.extendedProperties.recommendationOfferingId), shortDescriptionProblem = tostring(properties.shortDescription.problem), shortDescriptionSolution = tostring(properties.shortDescription.solution)";

        private readonly IConfiguration _config;
        private readonly IWorkItemClient _workItemClient;

        private readonly bool _useTriageRepoForUnmapped;
        private readonly string _unmappedRepository;

        private Dictionary<string, string>? _subscriptionToRepoMap;

        public GetRetirements(ILoggerFactory loggerFactory, IConfiguration config, Helpers.Azure.ManagementClient client, IWorkItemClient workItemClient, LifecycleClient lifecycleClient)
        {
            _logger = loggerFactory.CreateLogger<GetRetirements>();
            _managementClient = client;
            _config = config;
            _lifecycleClient = lifecycleClient;
            _workItemClient = workItemClient;

            _targetRepository = _config.GetSection(ConfigKeys.App.TargetRepository).Get<string>() ?? throw new InvalidOperationException("App:TargetRepository is not configured.");
            _workItemScope = Enum.Parse<WorkItemScope>(_config.GetSection(ConfigKeys.App.WorkItemScope).Get<string>() ?? "monolithic", true);
            _unmappedRepository = config.GetSection(ConfigKeys.App.UnmappedRepository).Get<string>() ?? string.Empty;

            string? mappingJson = _config.GetSection(ConfigKeys.App.TargetResourceGroupMapping).Get<string>();
            _rgRepoMapping = !string.IsNullOrEmpty(mappingJson)
                ? JsonSerializer.Deserialize<List<AzureRepositoryMap>>(mappingJson) ?? []
                : [];


            _lifecycleSignalsEnable = _config.GetSection(ConfigKeys.App.LifecycleSignalsEnable).Get<bool?>() ?? false;
            _lifecycleWarningWindow = TimeSpan.FromDays(_config.GetSection(ConfigKeys.App.LifecycleWarningWindowDays).Get<int?>() ?? 180);

            _assignCopilot = _config.GetSection(ConfigKeys.App.AssignGitHubCopilot).Get<bool?>() ?? false;
            _createParentWorkItems = config.GetSection(ConfigKeys.App.CreateParentWorkItems).Get<bool?>() ?? true;
            _createChildWorkItems = _config.GetSection(ConfigKeys.App.CreateChildWorkItems).Get<bool?>() ?? true;
            _httpEndpointEnable = _config.GetSection(ConfigKeys.App.HTTPEndpointEnable).Get<bool?>() ?? false;
            _httpEndpointOutput = _config.GetSection(ConfigKeys.App.HTTPEndpointOutput).Get<bool?>() ?? false;
            _httpEndpointWhatIf = _config.GetSection(ConfigKeys.App.HTTPEndpointWhatIf).Get<bool?>() ?? false;
            _useTriageRepoForUnmapped = config.GetSection(ConfigKeys.App.UseTriageRepoForUnmapped).Get<bool?>() ?? true;

            string? rg = _config.GetSection(ConfigKeys.App.TargetResourceGroup).Get<string>();
            _advisoryQuery = rg != null ? $"{_baseQuery} | where resourceGroup has \"{rg}\"" : _baseQuery;
        }

        [Function("GetRetirements")]
        public async Task RunTimer([TimerTrigger("%App:TimerTrigger%")] TimerInfo timerInfo)
        {
            Stopwatch sw = Stopwatch.StartNew();
            _logger.LogInformation("Retrieving Retirements via Timer");

            try
            {
                await GetRetirementsASync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Caught exception whilst trying to fetch retirements.\n{Exception}", ex);
            }

            sw.Stop();
            _logger.LogInformation("Function ran. Approximately took {ElapsedSeconds} second(s)", sw.Elapsed.TotalSeconds);
            _logger.LogInformation("Next timer schedule = {NextSchedule}", timerInfo.ScheduleStatus?.Next);
        }

        [Function("GetRetirementsManual")]
        public async Task<HttpResponseData> RunHttp([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            if (!_httpEndpointEnable)
            {
                _logger.LogDebug("Manual Endpoint hit when App:HTTPEndpointEnable is disabled");
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            bool whatIf = req.Query["whatIf"] == "true";

            if (whatIf && !_httpEndpointWhatIf)
            {
                _logger.LogDebug("Dry run requested, when App:HTTPEndpointWhatIf is disabled.");
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            if (whatIf)
            {
                _logger.LogInformation("[WhatIf] Performing a dry-run...");
            }

            Stopwatch sw = Stopwatch.StartNew();
            _logger.LogInformation("Retrieving Retirements via HTTP Manual trigger");

            try
            {
                GetRetirementsResponse retireResult = await GetRetirementsASync(whatIf);
                sw.Stop();

                retireResult.TimeElapsed = sw.Elapsed.TotalSeconds;

                var response = req.CreateResponse(retireResult.Result == GetRetirementsResult.Success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(_httpEndpointOutput ? retireResult : new GetRetirementsResponse() { Result = GetRetirementsResult.Success, ResultDescription = "Function ran successfully." });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError("Caught exception whilst handling request.\n{Exception}", ex);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);

                sw.Stop();

                GetRetirementsResponse retireResp = new GetRetirementsResponse() { Result = GetRetirementsResult.Failure, ResultDescription = "Error whilst completing action. Please check App Insights for more information." };

                if (_httpEndpointOutput)
                {
                    retireResp.ResultDescription = $"Caught exception whilst handling request.\n{ex}";
                    retireResp.TimeElapsed = sw.Elapsed.TotalSeconds;
                    retireResp.WhatIf = whatIf;
                }

                await response.WriteAsJsonAsync(retireResp);

                return response;
            }
            finally
            {
                _logger.LogInformation("Function ran. Approximately took {ElapsedSeconds} second(s)", sw.Elapsed.TotalSeconds);
                if (whatIf)
                {
                    _logger.LogInformation("[WhatIf] Dry-run complete");
                }
            }
        }

        private async Task<Dictionary<string, string>> BuildSubscriptionToRepoMapASync()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var mgMappings = _rgRepoMapping
            .Where(m => m.Type == AzureContainerType.ManagementGroup)
            .ToList();

            for (int i = 0; i < mgMappings.Count; i++)
            {
                var mapping = mgMappings[i];

                try
                {
                    var subscriptions = await _managementClient.GetManagementGroupSubscriptionsAsync(mapping.Name);
                    foreach (var (subId, mgId) in subscriptions)
                    {
                        map.TryAdd(subId, mgId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to resolve subscriptions for management group {GroupId}", mapping.Name);
                }
            }

            return map;
        }

        private string GetRepositoryForAdvisory(Advisory advisory)
        {
            if (_workItemScope != WorkItemScope.PerResourceGroup)
            {
                return _targetRepository;
            }

            AzureRepositoryMap? mapping = _rgRepoMapping.FirstOrDefault(m => m.Type switch
            {
                AzureContainerType.Subscription => string.Equals(m.Name, advisory.GetSubscriptionId(), StringComparison.OrdinalIgnoreCase),
                AzureContainerType.ResourceGroup => string.Equals(m.Name, advisory.GetResourceGroupName(), StringComparison.OrdinalIgnoreCase),
                AzureContainerType.ManagementGroup => _subscriptionToRepoMap != null && _subscriptionToRepoMap.TryGetValue(advisory.GetSubscriptionId(), out string? mgId) && string.Equals(mgId, m.Name, StringComparison.OrdinalIgnoreCase),
                _ => false
            });

            return mapping?.Repository ?? ((!_useTriageRepoForUnmapped && _unmappedRepository != string.Empty) ? _unmappedRepository : _targetRepository);
        }

        public async Task<GetRetirementsResponse> GetRetirementsASync(bool whatIf = false)
        {
            _logger.LogInformation("Running function at {CurrentTime}", DateTime.UtcNow);

            if (_rgRepoMapping.Any(m => m.Type == AzureContainerType.ManagementGroup))
            {
                _subscriptionToRepoMap = await BuildSubscriptionToRepoMapASync();
                _logger.LogInformation("Resolved {Count} subscriptions from management group mappings", _subscriptionToRepoMap.Count);
            }

            string[] subs = await _managementClient.GetSubscriptionsAsync();
            if (subs is null || subs.Length == 0)
            {
                _logger.LogWarning("No subscriptions returned; aborting.");

                return new GetRetirementsResponse() { Result = GetRetirementsResult.Failure, ResultDescription = "No subscriptions returned", WhatIf = whatIf };
            }

            List<Advisory> advisories = new List<Advisory>();

            foreach (string sub in subs)
            {
                QueryResult<RetirementData> data = await _managementClient.RunQueryAsync<RetirementData>(sub, _advisoryQuery);

                _logger.LogInformation("Subscription {SubscriptionId}: found {Count} retirement advisories", sub, data.Length);

                for (int i = 0; i < data.Length; i++)
                {
                    advisories.Add(data.Data[i].ToAdvisory());
                }
            }

            if (_lifecycleSignalsEnable)
            {
                List<Advisory> lifecycleAdvisories = await GetLifecycleAdvisoriesASync(subs);
                if (lifecycleAdvisories.Count > 0)
                {
                    _logger.LogInformation("Adding {Count} lifecycle advisories to the pipeline", lifecycleAdvisories.Count);
                    advisories.AddRange(lifecycleAdvisories);
                }
            }

            if (advisories.Count == 0)
            {
                _logger.LogInformation("No retirement advisories found. Nothing to process.");
                return new GetRetirementsResponse() { Result = GetRetirementsResult.Success, ResultDescription = "No retirement advisories found.", WhatIf = whatIf };
            }

            _logger.LogInformation("Found {Total} retirement advisories across {SubCount} subscription(s)", advisories.Count, subs.Length);
            Dictionary<string, List<Advisory>> advisoriesByRepo = advisories.GroupBy(GetRepositoryForAdvisory).ToDictionary(g => g.Key, g => g.ToList());

            // RecommendationTypeId -> (repo -> list of child work items)
            Dictionary<string, Dictionary<string, List<WorkItem>>> childItemsByType = [];

            // RecommendationTypeId -> representative advisory (for title/description)
            Dictionary<string, Advisory> representativeByType = [];

            List<WorkItem>? allExistingWorkItems = _httpEndpointOutput ? [] : null;
            List<WorkItem>? allCreatedWorkItems = _httpEndpointOutput ? [] : null;

            int totalAttempted = 0;
            int totalCreated = 0;

            foreach (var (repo, repoAdvisories) in advisoriesByRepo)
            {
                _logger.LogInformation("Processing {Count} advisories for repository {Repo}", repoAdvisories.Count, repo);

                Dictionary<string, WorkItem> existingWorkItems = await _workItemClient.FindExistingByAdvisoryAsync(repoAdvisories, repo);
                List<Advisory> advisoriesToCreate = repoAdvisories.Where(a => !existingWorkItems.ContainsKey(a.Name)).ToList();

                _logger.LogInformation("Found {ExistingCount} existing issues, creating {NewCount} new work items in {Repo}",
                    existingWorkItems.Count, advisoriesToCreate.Count, repo);

                List<(Advisory, WorkItem)> createdWorkItems = _createChildWorkItems ? await _workItemClient.CreateBatchAsync(advisoriesToCreate, repo, _assignCopilot, whatIf) ?? [] : [];

                totalAttempted += advisoriesToCreate.Count;
                totalCreated += createdWorkItems.Count;

                if (_createChildWorkItems && advisoriesToCreate.Count > 0 && createdWorkItems.Count == 0)
                {
                    _logger.LogError("All {Count} work item creations failed for repository {Repo}", advisoriesToCreate.Count, repo);
                    return new GetRetirementsResponse()
                    {
                        Result = GetRetirementsResult.Failure,
                        ResultDescription = $"All work item creations failed for {repo}. Check authentication and permissions.",
                        WhatIf = whatIf
                    };
                }

                if (_createParentWorkItems)
                {
                    // Map existing issues back to their advisory by name
                    var existingPairs = existingWorkItems.Select(kvp =>
                    {
                        var advisory = repoAdvisories.First(a => a.Name == kvp.Key);
                        return (advisory, workItem: kvp.Value);
                    });

                    foreach (var (advisory, workItem) in createdWorkItems.Concat(existingPairs))
                    {
                        string typeId = advisory.Properties.RecommendationTypeId;

                        if (!childItemsByType.ContainsKey(typeId))
                        {
                            childItemsByType[typeId] = [];
                            representativeByType[typeId] = advisory;
                        }

                        if (!childItemsByType[typeId].ContainsKey(repo))
                        {
                            childItemsByType[typeId][repo] = [];
                        }

                        childItemsByType[typeId][repo].Add(workItem);
                    }
                }

                if (_httpEndpointOutput)
                {
                    allExistingWorkItems!.AddRange(existingWorkItems.Values);
                    allCreatedWorkItems!.AddRange(createdWorkItems.Select(wk => wk.Item2));
                }
            }

            List<ParentWorkItemResult>? parentWorkItems = _createParentWorkItems ? new List<ParentWorkItemResult>() : null;

            if (_createParentWorkItems)
            {
                foreach (var (typeId, childItemsByRepo) in childItemsByType)
                {
                    ParentWorkItemResult? result = await _workItemClient.FindOrCreateParentAsync(
                        typeId,
                        representativeByType[typeId],
                        childItemsByRepo,
                        _targetRepository,
                        whatIf);

                    if (result != null)
                    {
                        parentWorkItems!.Add(result);
                    }
                }
            }

            bool allItemsCreatedSuccessfully = totalAttempted == totalCreated;

            GetRetirementsResponse response = new GetRetirementsResponse() { Result = allItemsCreatedSuccessfully ? GetRetirementsResult.Success : GetRetirementsResult.Failure, ResultDescription = allItemsCreatedSuccessfully ? "Function ran with no issues." : "Some work items failed to be created. Please check logs." };

            if (_httpEndpointOutput)
            {
                response.Advisories = advisories;
                response.ExistingWorkItems = allExistingWorkItems;
                response.NewWorkItems = allCreatedWorkItems;
                response.ParentWorkItems = parentWorkItems;
                response.WhatIf = whatIf;
            }

            return response;
        }

        public async Task<List<Advisory>> GetLifecycleAdvisoriesASync(string[] subscriptions)
        {
            var result = new List<Advisory>();

            DateTime now = DateTime.UtcNow;

            var aksEntries = await _lifecycleClient.GetProductEntriesASync(LifecycleProduct.AksKubernetes);
            var pgEntries = await _lifecycleClient.GetProductEntriesASync(LifecycleProduct.AzurePostgreSQLFlexible);

            var aksByVersion = aksEntries.GroupBy(e => e.Version).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var pgByVersion = pgEntries.GroupBy(e => e.Version).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (string sub in subscriptions)
            {
                if (aksByVersion.Count > 0)
                {
                    QueryResult<VersionedResource> aksResources = await _managementClient.RunQueryAsync<VersionedResource>(sub, _aksResourceQuery);
                    _logger.LogInformation("Subscription {SubscriptionId}: inspecting {Count} AKS clusters for lifecycle signals", sub, aksResources.Length);
                    foreach (var resource in aksResources.Data)
                    {
                        Advisory? advisory = LifecycleAdvisoryGenerator.TryCreate(resource, aksByVersion, LifecycleProduct.AksKubernetes, now, _lifecycleWarningWindow);
                        if (advisory != null) result.Add(advisory);
                    }
                }

                if (pgByVersion.Count > 0)
                {
                    QueryResult<VersionedResource> pgResources = await _managementClient.RunQueryAsync<VersionedResource>(sub, _postgreSqlResourceQuery);
                    _logger.LogInformation("Subscription {SubscriptionId}: inspecting {Count} PostgreSQL flexible servers for lifecycle signals", sub, pgResources.Length);
                    foreach (var resource in pgResources.Data)
                    {
                        Advisory? advisory = LifecycleAdvisoryGenerator.TryCreate(resource, pgByVersion, LifecycleProduct.AzurePostgreSQLFlexible, now, _lifecycleWarningWindow);
                        if (advisory != null) result.Add(advisory);
                    }
                }
            }

            return result;
        }
    }
}
