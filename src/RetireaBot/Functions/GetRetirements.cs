using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Helpers.Orchestration;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.HTTP;
using Microsoft.RetireaBot.Models.Azure;
using System.Diagnostics;
using System.Net;
using Microsoft.RetireaBot.Helpers.Lifecycle;
using Microsoft.RetireaBot.Models.Lifecycle;

namespace Microsoft.RetireaBot.Functions
{
    public class GetRetirements
    {
        private readonly ILogger _logger;
        private readonly LifecycleClient _lifecycleClient;
        private readonly Helpers.Azure.ManagementClient _managementClient;
        private readonly IBackendOrchestrator _orchestrator;
        private readonly IDataSinkOrchestrator _sinkOrchestrator;

        private readonly bool _httpEndpointEnable;
        private readonly bool _httpEndpointOutput;
        private readonly bool _httpEndpointWhatIf;

        private readonly bool _lifecycleSignalsEnable;
        private readonly TimeSpan _lifecycleWarningWindow;

        private const string _advisoryQuery = "advisorresources | where properties.extendedProperties.recommendationSubCategory == \"ServiceUpgradeAndRetirement\" | where tostring(properties.category) has \"HighAvailability\" | extend resourceId = tostring(properties.resourceMetadata.resourceId) | project id, name, type, subscriptionId, resourceGroup, location, resourceId, ServiceID = tostring(properties.recommendationTypeId), impact = tostring(properties.impact), category = tostring(properties.category), impactedField = tostring(properties.impactedField), impactedValue = tostring(properties.impactedValue), lastUpdated = tostring(properties.lastUpdated), retirementDate = tostring(properties.extendedProperties.retirementDate), retirementFeatureName = tostring(properties.extendedProperties.retirementFeatureName), maturityLevel = tostring(properties.extendedProperties.maturityLevel), recommendationOfferingId = tostring(properties.extendedProperties.recommendationOfferingId), shortDescriptionProblem = tostring(properties.shortDescription.problem), shortDescriptionSolution = tostring(properties.shortDescription.solution)";
        private const string _aksResourceQuery = "resources | where type =~ 'microsoft.containerservice/managedclusters' | project id, name, type, subscriptionId, resourceGroup, location, version = tostring(properties.kubernetesVersion)";
        private const string _postgreSqlResourceQuery = "resources | where type =~ 'microsoft.dbforpostgresql/flexibleservers' | project id, name, type, subscriptionId, resourceGroup, location, version = tostring(properties.version)";

        public GetRetirements(ILoggerFactory loggerFactory, IConfiguration config, Helpers.Azure.ManagementClient client, IBackendOrchestrator orchestrator, IDataSinkOrchestrator sinkOrchestrator, LifecycleClient lifecycleClient)
        {
            _logger = loggerFactory.CreateLogger<GetRetirements>();
            _managementClient = client;
            _lifecycleClient = lifecycleClient;
            _orchestrator = orchestrator;
            _sinkOrchestrator = sinkOrchestrator;

            _lifecycleSignalsEnable = config.GetSection(ConfigKeys.App.LifecycleSignalsEnable).Get<bool?>() ?? false;
            _lifecycleWarningWindow = TimeSpan.FromDays(config.GetSection(ConfigKeys.App.LifecycleWarningWindowDays).Get<int?>() ?? 180);

            _httpEndpointEnable = config.GetSection(ConfigKeys.App.HTTPEndpointEnable).Get<bool?>() ?? false;
            _httpEndpointOutput = config.GetSection(ConfigKeys.App.HTTPEndpointOutput).Get<bool?>() ?? false;
            _httpEndpointWhatIf = config.GetSection(ConfigKeys.App.HTTPEndpointWhatIf).Get<bool?>() ?? false;
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

        public async Task<GetRetirementsResponse> GetRetirementsASync(bool whatIf = false)
        {
            _logger.LogInformation("Running function at {CurrentTime}", DateTime.UtcNow);

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

            IReadOnlyList<BackendOutputResult> outputs = await _orchestrator.RunAsync(advisories, whatIf);
            IReadOnlyList<DataSinkOutputResult> sinkOutput = await _sinkOrchestrator.RunAsync(advisories, whatIf);

            GetRetirementsResult overall =
                outputs.All(o => o.Status == GetRetirementsResult.Success) && sinkOutput.All(o => o.Status == GetRetirementsResult.Success) ? GetRetirementsResult.Success :
                outputs.All(o => o.Status == GetRetirementsResult.Failure) && sinkOutput.All(o => o.Status == GetRetirementsResult.Failure) ? GetRetirementsResult.Failure :
                                                                             GetRetirementsResult.Partial;

            string description = overall switch
            {
                GetRetirementsResult.Success => "Function ran with no issues.",
                GetRetirementsResult.Partial => "Some backends reported failures. See Outputs for details.",
                _ => "All backends failed. Check logs."
            };

            GetRetirementsResponse response = new GetRetirementsResponse()
            {
                Result = overall,
                ResultDescription = description,
                WhatIf = whatIf,
            };

            if (_httpEndpointOutput)
            {
                response.Advisories = advisories;
                response.BackendOutputs = outputs.ToList();
                response.SinkOutputs = sinkOutput.ToList();
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