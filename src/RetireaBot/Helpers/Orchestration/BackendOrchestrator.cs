using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Domain;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;


namespace Microsoft.RetireaBot.Helpers.Orchestration
{
    public class BackendOrchestrator : IBackendOrchestrator
    {
        private readonly ILogger<BackendOrchestrator> _logger;
        private readonly IReadOnlyList<IWorkItemClient> _workItemClients;
        private readonly IVendorSettingsProvider _vendorSettings;
        private readonly Helpers.Azure.ManagementClient _managementClient;

        private readonly bool _createParentWorkItems;
        private readonly bool _createChildWorkItems;
        private readonly bool _useTriageRepoForUnmapped;
        private readonly WorkItemScope _workItemScope;

        public BackendOrchestrator(
            ILoggerFactory loggerFactory,
            IConfiguration config,
            IEnumerable<IWorkItemClient> workItemClients,
            IVendorSettingsProvider vendorSettings,
            Helpers.Azure.ManagementClient managementClient)
        {
            _logger = loggerFactory.CreateLogger<BackendOrchestrator>();
            _workItemClients = workItemClients.ToList();
            _vendorSettings = vendorSettings;
            _managementClient = managementClient;

            _createParentWorkItems = config.GetSection(ConfigKeys.App.CreateParentWorkItems).Get<bool?>() ?? true;
            _createChildWorkItems = config.GetSection(ConfigKeys.App.CreateChildWorkItems).Get<bool?>() ?? true;
            _useTriageRepoForUnmapped = config.GetSection(ConfigKeys.App.UseTriageRepoForUnmapped).Get<bool?>() ?? true;
            _workItemScope = Enum.Parse<WorkItemScope>(
                config.GetSection(ConfigKeys.App.WorkItemScope).Get<string>() ?? nameof(WorkItemScope.Monolithic),
                ignoreCase: true);
        }

        public async Task<IReadOnlyList<BackendOutputResult>> RunAsync(
            List<Advisory> advisories,
            bool whatIf,
            CancellationToken cancellationToken = default)
        {
            if (_workItemClients.Count < 1)
            {
                _logger.LogInformation("No work item clients registered; skipping");
                return [];
            }

            IReadOnlyDictionary<string, string> subscriptionToMgMap =
                _workItemScope == WorkItemScope.PerContainer
                    ? await ResolveSubscriptionToMgMapAsync(cancellationToken)
                    : new Dictionary<string, string>();

            var tasks = _workItemClients
                .Select(c => ProcessBackendAsync(c, advisories, subscriptionToMgMap, whatIf, cancellationToken));

            return await Task.WhenAll(tasks);
        }

        private async Task<BackendOutputResult> ProcessBackendAsync(
            IWorkItemClient workItemClient,
            List<Advisory> advisories,
            IReadOnlyDictionary<string, string> subscriptionToMgMap,
            bool whatIf,
            CancellationToken cancellationToken)
        {
            string backendName = workItemClient.Backend.ToString();

            var output = new BackendOutputResult
            {
                BackendName = backendName,
                Status = GetRetirementsResult.Success,
            };

            _logger.LogInformation("Processing backend {Backend}", backendName);

            try
            {
                IVendorSettings vendor = _vendorSettings.For(workItemClient.Backend);

                Dictionary<string, List<Advisory>> advisoriesByRepo =
                    RouteAdvisoriesForBackend(vendor, advisories, subscriptionToMgMap);

                // Per-backend trees so parents only link children created by the same backend.
                var childItemsByType = new Dictionary<string, Dictionary<string, List<WorkItem>>>();
                var representativeByType = new Dictionary<string, Advisory>();

                (int attempted, int created) = await ProcessChildWorkItemsAsync(
                    workItemClient,
                    advisoriesByRepo,
                    whatIf,
                    output,
                    childItemsByType,
                    representativeByType,
                    cancellationToken);

                if (_createParentWorkItems)
                {
                    await CreateParentWorkItemsAsync(
                        workItemClient,
                        vendor,
                        childItemsByType,
                        representativeByType,
                        whatIf,
                        output,
                        cancellationToken);
                }

                if (_createChildWorkItems && attempted > 0)
                {
                    if (created == 0)
                    {
                        output.Status = GetRetirementsResult.Failure;
                        output.Error = $"All {attempted} work item creations failed for backend {backendName}.";
                    }
                    else if (created < attempted)
                    {
                        output.Status = GetRetirementsResult.Partial;
                        output.Error = $"{attempted - created} of {attempted} work item creations failed for backend {backendName}.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backend {Backend} failed unexpectedly", backendName);
                output.Status = GetRetirementsResult.Failure;
                output.Error = ex.Message;
            }

            return output;
        }

        private async Task<(int attempted, int created)> ProcessChildWorkItemsAsync(
            IWorkItemClient workItemClient,
            Dictionary<string, List<Advisory>> advisoriesByRepo,
            bool whatIf,
            BackendOutputResult output,
            Dictionary<string, Dictionary<string, List<WorkItem>>> childItemsByType,
            Dictionary<string, Advisory> representativeByType,
            CancellationToken cancellationToken)
        {
            int attempted = 0;
            int created = 0;

            foreach (var (repo, repoAdvisories) in advisoriesByRepo)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation(
                    "[{Backend}] Processing {Count} advisories for repository {Repo}",
                    output.BackendName, repoAdvisories.Count, repo);

                Dictionary<string, WorkItem> existingWorkItems =
                    await workItemClient.FindExistingByAdvisoryAsync(repoAdvisories, repo);

                List<Advisory> advisoriesToCreate = repoAdvisories
                    .Where(a => !existingWorkItems.ContainsKey(a.Name))
                    .ToList();

                _logger.LogInformation(
                    "[{Backend}] Found {ExistingCount} existing issues, creating {NewCount} new work items in {Repo}",
                    output.BackendName, existingWorkItems.Count, advisoriesToCreate.Count, repo);

                bool assignCopilot = _vendorSettings.For(workItemClient.Backend).AssignCopilot;

                List<(Advisory, WorkItem)> createdWorkItems = _createChildWorkItems
                    ? await workItemClient.CreateBatchAsync(advisoriesToCreate, repo, assignCopilot, whatIf) ?? []
                    : [];

                attempted += advisoriesToCreate.Count;
                created += createdWorkItems.Count;

                if (_createChildWorkItems && advisoriesToCreate.Count > 0 && createdWorkItems.Count == 0)
                {
                    _logger.LogError(
                        "[{Backend}] All {Count} work item creations failed for repository {Repo}",
                        output.BackendName, advisoriesToCreate.Count, repo);
                }

                if (_createParentWorkItems)
                {
                    // Map existing issues back to their advisory by name.
                    var existingPairs = existingWorkItems.Select(kvp =>
                    {
                        var advisory = repoAdvisories.First(a => a.Name == kvp.Key);
                        return (advisory, workItem: kvp.Value);
                    });

                    foreach (var (advisory, workItem) in createdWorkItems.Concat(existingPairs))
                    {
                        string typeId = advisory.Properties.RecommendationTypeId;

                        if (!childItemsByType.TryGetValue(typeId, out var byRepo))
                        {
                            byRepo = new Dictionary<string, List<WorkItem>>();
                            childItemsByType[typeId] = byRepo;
                            representativeByType[typeId] = advisory;
                        }

                        if (!byRepo.TryGetValue(repo, out var bucket))
                        {
                            bucket = new List<WorkItem>();
                            byRepo[repo] = bucket;
                        }

                        bucket.Add(workItem);
                    }
                }

                output.Existing.AddRange(existingWorkItems.Values);
                output.Created.AddRange(createdWorkItems.Select(wk => wk.Item2));
            }

            return (attempted, created);
        }

        private async Task CreateParentWorkItemsAsync(
            IWorkItemClient workItemClient,
            IVendorSettings vendor,
            Dictionary<string, Dictionary<string, List<WorkItem>>> childItemsByType,
            Dictionary<string, Advisory> representativeByType,
            bool whatIf,
            BackendOutputResult output,
            CancellationToken cancellationToken)
        {
            foreach (var (typeId, childItemsByRepo) in childItemsByType)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ParentWorkItemResult? result = await workItemClient.FindOrCreateParentAsync(
                    typeId,
                    representativeByType[typeId],
                    childItemsByRepo,
                    vendor.TargetRepository,
                    whatIf);

                if (result != null)
                {
                    output.Parents.Add(result);
                }
            }
        }

        private Dictionary<string, List<Advisory>> RouteAdvisoriesForBackend(
            IVendorSettings vendor,
            List<Advisory> advisories,
            IReadOnlyDictionary<string, string> subscriptionToMgMap)
        {
            var byRepo = new Dictionary<string, List<Advisory>>(StringComparer.OrdinalIgnoreCase);

            foreach (Advisory advisory in advisories)
            {
                // Per-backend ResourceGroup filter: skip advisories outside the configured RG (substring match for parity with KQL `has`).
                if (!string.IsNullOrEmpty(vendor.TargetResourceGroup)
                    && advisory.GetResourceGroupName().IndexOf(vendor.TargetResourceGroup, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string repo = ResolveRepoForAdvisory(vendor, advisory, subscriptionToMgMap);

                if (!byRepo.TryGetValue(repo, out var bucket))
                {
                    bucket = new List<Advisory>();
                    byRepo[repo] = bucket;
                }
                bucket.Add(advisory);
            }

            return byRepo;
        }

        private string ResolveRepoForAdvisory(
            IVendorSettings vendor,
            Advisory advisory,
            IReadOnlyDictionary<string, string> subscriptionToMgMap)
        {
            if (_workItemScope != WorkItemScope.PerContainer)
            {
                return vendor.TargetRepository;
            }

            AzureRepositoryMap? mapping = vendor.TargetContainerMapping.FirstOrDefault(m => m.Type switch
            {
                AzureContainerType.Subscription => string.Equals(m.Name, advisory.GetSubscriptionId(), StringComparison.OrdinalIgnoreCase),
                AzureContainerType.ResourceGroup => string.Equals(m.Name, advisory.GetResourceGroupName(), StringComparison.OrdinalIgnoreCase),
                AzureContainerType.ManagementGroup => subscriptionToMgMap.TryGetValue(advisory.GetSubscriptionId(), out string? mgId)
                    && string.Equals(mgId, m.Name, StringComparison.OrdinalIgnoreCase),
                _ => false
            });

            if (mapping != null)
            {
                return mapping.Repository;
            }

            return (!_useTriageRepoForUnmapped && !string.IsNullOrEmpty(vendor.UnmappedRepository))
                ? vendor.UnmappedRepository
                : vendor.TargetRepository;
        }

        private async Task<IReadOnlyDictionary<string, string>> ResolveSubscriptionToMgMapAsync(CancellationToken cancellationToken)
        {
            var mgIds = _workItemClients
                .SelectMany(c => _vendorSettings.For(c.Backend).TargetContainerMapping)
                .Where(m => m.Type == AzureContainerType.ManagementGroup)
                .Select(m => m.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string mgId in mgIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    Dictionary<string, string> subs = await _managementClient.GetManagementGroupSubscriptionsAsync(mgId);
                    foreach (var (subId, resolvedMgId) in subs)
                    {
                        map.TryAdd(subId, resolvedMgId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to resolve subscriptions for management group {GroupId}", mgId);
                }
            }

            _logger.LogInformation("Resolved {Count} subscriptions across {GroupCount} management group(s)", map.Count, mgIds.Count);
            return map;
        }
    }
}