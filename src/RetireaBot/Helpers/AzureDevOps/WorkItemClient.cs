using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;
using Microsoft.RetireaBot.Models.HTTP;
using System.Text.RegularExpressions;

namespace Microsoft.RetireaBot.Helpers.AzureDevOps
{
    public partial class WorkItemClient : IWorkItemClient
    {
        private readonly CredentialProvider _credentialProvider;
        private readonly ILogger _logger;

        private readonly string _advisoryLabel;
        private readonly string _advisoryParentLabel;
        private readonly string _advisoryLabelPrefix;
        private readonly string _parentLabelPrefix;

        private readonly string _workItemDefaultAssignee;
        private readonly string _workItemOpenState;
        private readonly string _workItemClosedState;
        private readonly string _workItemType;

        private const int MaxTagLength = 400;

        public WorkItemClient(IConfiguration config, CredentialProvider credentialProvider, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<WorkItemClient>();

            _credentialProvider = credentialProvider;

            _advisoryLabel = config.GetSection(ConfigKeys.App.AdvisoryLabel).Get<string>() ?? "azure-advisor";
            _advisoryParentLabel = config.GetSection(ConfigKeys.App.AdvisoryParentLabel).Get<string>() ?? "tracking";
            _advisoryLabelPrefix = config.GetSection(ConfigKeys.App.AdvisoryLabelPrefix).Get<string>() ?? "advisor-";
            _parentLabelPrefix = config.GetSection(ConfigKeys.App.ParentLabelPrefix).Get<string>() ?? "advisor-type-";

            _workItemDefaultAssignee = config.GetSection(ConfigKeys.AzureDevOps.WorkItemDefaultAssignee).Get<string>() ?? "";
            _workItemOpenState = config.GetSection(ConfigKeys.AzureDevOps.WorkItemOpenState).Get<string>() ?? "New";
            _workItemClosedState = config.GetSection(ConfigKeys.AzureDevOps.WorkItemClosedState).Get<string>() ?? "Closed";
            _workItemType = config.GetSection(ConfigKeys.AzureDevOps.WorkItemType).Get<string>() ?? "Task";
        }

        private async Task<WorkItemTrackingHttpClient> GetWorkItemClient()
        {
            var connection = await _credentialProvider.GetConnection();
            return connection.GetClient<WorkItemTrackingHttpClient>();
        }

        private Models.WorkItem ToWorkItem(Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem workItem)
        {
            var fields = workItem.Fields;

            string stateValue = fields.GetValueOrDefault("System.State")?.ToString() ?? "";
            string tags = fields.GetValueOrDefault("System.Tags")?.ToString() ?? "";
            IdentityRef? assignedTo = fields.GetValueOrDefault("System.AssignedTo") as IdentityRef;

            return new Models.WorkItem()
            {
                Id = workItem.Id?.ToString() ?? "",
                Number = workItem.Id ?? 0,
                Title = fields.GetValueOrDefault("System.Title")?.ToString() ?? "",
                Body = fields.GetValueOrDefault("System.Description")?.ToString() ?? "",
                State = stateValue.Equals(_workItemClosedState, StringComparison.OrdinalIgnoreCase)
            ? WorkItemState.Closed
            : WorkItemState.Open,
                Labels = string.IsNullOrEmpty(tags)
            ? []
            : tags.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
                Assignees = assignedTo != null
            ? [assignedTo.UniqueName]
            : [],
                Url = workItem.Links?.Links?.ContainsKey("html") == true
            ? ((ReferenceLink)workItem.Links.Links["html"]).Href
            : workItem.Url
            };
        }

        private static Models.WorkItem CreateWhatIf(string title, string body, List<string> labels, List<string> assignees)
            => new Models.WorkItem()
            {
                Id = "",
                Number = 0,
                Title = title,
                Body = body,
                Labels = labels,
                Assignees = assignees,
                State = WorkItemState.Open,
                Url = null
            };


        /// <summary>
        /// Generates the body for the parent work item, with the description of the advisory.
        /// </summary>
        private string GenerateParentWorkItemBody(Advisory representativeAdvisory, string parentRepo)
        {
            var props = representativeAdvisory.Properties;

            List<string> detailsList = new List<string>();

            if (!String.IsNullOrEmpty(props.ExtendedProperties?.RetirementDate))
                detailsList.Add($"<strong>Retirement Date:</strong> {props.ExtendedProperties?.RetirementDate}");

            if (!String.IsNullOrEmpty(props.ExtendedProperties?.RetirementFeatureName))
                detailsList.Add($"<strong>Retirement Feature:</strong> {props.ExtendedProperties?.RetirementFeatureName}");


            return $@"<h2>Retirement Tracking: {props.ShortDescription.Problem}</h2>

<p><strong>Impact:</strong> {props.Impact}<br>
<strong>Category:</strong> {props.Category}<br>

{string.Join("<br>", detailsList)}

<h3>Description</h3>
<p>{props.ShortDescription.Problem}</p>

<h3>Solution</h3>
<p>{props.ShortDescription.Solution}</p>

<h3>Recommendation Type ID</h3>
<code>{props.RecommendationTypeId}</code>

<h3>Last Updated</h3>
<code>{DateTime.UtcNow:r}</code>
";
        }

        private string GenerateWorkItemBody(Advisory advisory)
        {
            var props = advisory.Properties;
            return $@"<h2>Azure Advisor Recommendation</h2>
<p><strong>Impact:</strong> {props.Impact}<br>
<strong>Category:</strong> {props.Category}<br>
<strong>Resource:</strong> {props.ImpactedValue}</p>

<h3>Description</h3>
<p>{props.ShortDescription.Problem}</p>

<h3>Solution</h3>
<p>{props.ShortDescription.Solution}</p>

<h3>Details</h3>
<ul>
<li><strong>Retirement Date:</strong> {props.ExtendedProperties?.RetirementDate}</li>
<li><strong>Retirement Feature:</strong> {props.ExtendedProperties?.RetirementFeatureName}</li>
<li><strong>Resource ID:</strong> {props.ResourceMetadata?.ResourceId}</li>
<li><strong>Last Updated:</strong> {props.LastUpdated}</li>
</ul>

<h3>Advisory ID</h3>
<code>{advisory.Name}</code>";
        }

        private async Task<List<string>> ParseParentChildItems(int parentId, WorkItemTrackingHttpClient workItemClient)
        {
            var parent = await workItemClient.GetWorkItemAsync(parentId, expand: WorkItemExpand.Relations);

            var childIds = parent.Relations?
                .Where(r => r.Rel == "System.LinkTypes.Hierarchy-Forward")
                .Select(r => int.Parse(r.Url.Split('/').Last()))
                .ToList() ?? [];

            if (childIds.Count > 0)
            {
                var children = await workItemClient.GetWorkItemsAsync(childIds,
                    fields: ["System.Id"]);
                var wkiIds = children.Select(wki => wki.Id.ToString()) ?? [];

                return wkiIds.ToList()!;
            }

            return [];
        }

        public async Task<List<(Advisory, Models.WorkItem)>> CreateBatchAsync(List<Advisory> advisories, string targetRepo, bool assignCopilot, bool whatIf)
        {
            SemaphoreSlim semaphore = new SemaphoreSlim(5);

            var created = advisories.Select(async advisory =>
            {
                await semaphore.WaitAsync();

                var workItemClient = await GetWorkItemClient();

                try
                {
                    if (whatIf)
                    {
                        _logger.LogInformation("[WhatIf] Would create work item for advisory {AdvisoryId}: {Title}", advisory.Name, WorkItemClientCommon.GenerateWorkItemTitle(advisory));

                        return CreateWhatIf(WorkItemClientCommon.GenerateWorkItemTitle(advisory), GenerateWorkItemBody(advisory) ?? string.Empty, [WorkItemClientCommon.GenerateAdvisoryLabel(_advisoryLabelPrefix, advisory.Name, MaxTagLength), _advisoryLabel, advisory.Properties.Impact.ToLower()], [_workItemDefaultAssignee]);
                    }

                    JsonPatchDocument workItemPatch = new JsonPatchDocument
                    {
                        new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Title", Value = WorkItemClientCommon.GenerateWorkItemTitle(advisory)},
                        new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Tags", Value = $"{WorkItemClientCommon.GenerateAdvisoryLabel(_advisoryLabelPrefix, advisory.Name, MaxTagLength)};{_advisoryLabel};{advisory.Properties.Impact.ToLower()}"},
                        new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.State", Value = _workItemOpenState},
                        new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.AssignedTo", Value = _workItemDefaultAssignee},
                        new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Description", Value = GenerateWorkItemBody(advisory)},
                    };

                    var workItem = await workItemClient.CreateWorkItemAsync(workItemPatch, targetRepo, _workItemType);
                    _logger.LogInformation("Successfully created work item for {advisoryName}, {workItemID}", advisory.Name, workItem.Id);

                    return ToWorkItem(workItem);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create work item for advisory {AdvisoryId}", advisory.Name);
                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(created);

            if (results == null)
            {
                return new List<(Advisory, Models.WorkItem)>();
            }

            return [.. results.Select((r, i) => (advisory: advisories[i], wi: r))
                  .Where(p => p.wi != null)
                  .Select(p => (p.advisory, p.wi!))];
        }

        public async Task<Dictionary<string, Models.WorkItem>> FindExistingByAdvisoryAsync(List<Advisory> advisories, string targetRepo)
        {
            Dictionary<string, Models.WorkItem> existingWorkItems = new Dictionary<string, Models.WorkItem>();
            const int batchSize = 5;

            var workItemClient = await GetWorkItemClient();

            for (int i = 0; i < advisories.Count; i += batchSize)
            {
                var batch = advisories.Skip(i).Take(batchSize).ToList();
                var tagClauses = batch.Select(a => $"[System.Tags] CONTAINS '{SanitiseWiQLInput(WorkItemClientCommon.GenerateAdvisoryLabel(_advisoryLabelPrefix, a.Name, MaxTagLength))}'");
                var wiql = new Wiql
                {
                    Query = $"SELECT [System.Id] FROM WorkItems WHERE ({string.Join(" OR ", tagClauses)})"
                };

                try
                {
                    var result = await workItemClient.QueryByWiqlAsync(wiql, targetRepo);

                    if (result.WorkItems.Any())
                    {
                        var ids = result.WorkItems.Select(wi => wi.Id).ToList();
                        var workItems = await workItemClient.GetWorkItemsAsync(ids, fields: ["System.Id", "System.Title", "System.Tags", "System.State", "System.Description", "System.AssignedTo"]);

                        foreach (var advisory in batch)
                        {
                            string advisoryTag = WorkItemClientCommon.GenerateAdvisoryLabel(_advisoryLabelPrefix, advisory.Name, MaxTagLength);

                            var matchedWorkItem = workItems.FirstOrDefault(wi =>
                            {
                                var tags = wi.Fields.GetValueOrDefault("System.Tags")?.ToString() ?? "";
                                return tags.Split(";", StringSplitOptions.TrimEntries)
                                .Any(t => t.Equals(advisoryTag, StringComparison.OrdinalIgnoreCase));
                            });

                            if (matchedWorkItem != null)
                            {
                                existingWorkItems[advisory.Name] = ToWorkItem(matchedWorkItem);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to search for existing work items in batch.");
                }

                // Be reasonable with the query requests
                if (i + batchSize < advisories.Count)
                {
                    await Task.Delay(2000);
                }
            }
            return existingWorkItems;
        }

        public async Task<ParentWorkItemResult?> FindOrCreateParentAsync(string recommendationTypeId, Advisory representativeAdvisory, Dictionary<string, List<Models.WorkItem>> childItemsByRepo, string parentRepo, bool whatIf)
        {
            string parentLabel = WorkItemClientCommon.GenerateAdvisoryLabel(_parentLabelPrefix, recommendationTypeId, MaxTagLength);
            Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem? existingParent = null;

            var workItemClient = await GetWorkItemClient();

            var wiql = new Wiql
            {
                Query = $"SELECT [System.Id] FROM WorkItems WHERE [System.Tags] CONTAINS '{SanitiseWiQLInput(parentLabel)}' AND [System.Tags] CONTAINS '{SanitiseWiQLInput(_advisoryParentLabel)}' AND [System.Tags] CONTAINS '{SanitiseWiQLInput(_advisoryLabel)}'"
            };

            var result = await workItemClient.QueryByWiqlAsync(wiql, parentRepo);

            if (result.WorkItems.Any())
            {
                var wir = result.WorkItems.FirstOrDefault();

                if (wir != null)
                {
                    existingParent = await workItemClient.GetWorkItemAsync(wir.Id, fields: ["System.Id", "System.Title", "System.Tags", "System.State", "System.Description", "System.AssignedTo"]);
                    var parsedParent = ToWorkItem(existingParent);

                    var existingItems = await ParseParentChildItems(wir.Id, workItemClient);

                    List<ChildWorkItemReference> allRefs = childItemsByRepo.SelectMany(kvp => kvp.Value.Select(wki => new ChildWorkItemReference(kvp.Key, wki.Id))).ToList();
                    List<ChildWorkItemReference> newRefs = allRefs.Where(r => !existingItems.Contains(r.WorkItemId)).ToList();

                    if (newRefs.Count == 0)
                    {
                        _logger.LogInformation("Parent work item #{Number} for recommendation {TypeId} is already up to date", parsedParent.Number, recommendationTypeId);
                        return new ParentWorkItemResult()
                        {
                            Action = ParentWorkItemAction.Unchanged,
                            ChildCount = allRefs.Count,
                            RecommendationTypeId = recommendationTypeId,
                            WorkItem = ToWorkItem(existingParent)
                        };
                    }

                    string updatedBody = LastUpdatedFormat().Replace(parsedParent.Body, $"<h3>Last Updated</h3>\n<code>{DateTime.UtcNow:r}</code>");

                    JsonPatchDocument workItemPatch = new JsonPatchDocument
                        {
                            new JsonPatchOperation {Operation = Operation.Replace, Path = "/fields/System.Description", Value = updatedBody},
                        };

                    foreach (var currentItem in newRefs)
                    {
                        workItemPatch.Add(new JsonPatchOperation
                        {
                            Operation = Operation.Add,
                            Path = "/relations/-",
                            Value = new
                            {
                                rel = "System.LinkTypes.Hierarchy-Forward",
                                url = $"{_credentialProvider.GetConnectionUri()}/{currentItem.ProjectName}/_apis/wit/workItems/{currentItem.WorkItemId}",
                                attributes = new { comment = "Auto-linked by Retirebot" }
                            }
                        });
                    }

                    // Reopen if it was closed, since new resources appeared
                    if (parsedParent.State == WorkItemState.Closed)
                    {
                        workItemPatch.Add(new JsonPatchOperation { Operation = Operation.Replace, Path = "/fields/System.State", Value = _workItemOpenState });
                        _logger.LogInformation("Reopening parent work item #{Number} — new affected resources found", parsedParent.Number);
                    }

                    if (whatIf)
                    {
                        _logger.LogInformation("[WhatIf] Parent work item #{Number} with {Count} (vs {ExistingCount}) new child references for recommendation {TypeId} would be updated", parsedParent.Number, newRefs.Count, existingItems.Count, recommendationTypeId);

                        Models.WorkItem newWorkItem = ToWorkItem(existingParent);

                        newWorkItem.Body = updatedBody;
                        newWorkItem.State = WorkItemState.Open;


                        return new ParentWorkItemResult()
                        {
                            Action = ParentWorkItemAction.Updated,
                            ChildCount = allRefs.Count,
                            RecommendationTypeId = recommendationTypeId,
                            WorkItem = newWorkItem
                        };
                    }

                    var updated = await workItemClient.UpdateWorkItemAsync(workItemPatch, int.Parse(parsedParent.Id));

                    return new ParentWorkItemResult()
                    {
                        Action = ParentWorkItemAction.Updated,
                        ChildCount = allRefs.Count,
                        RecommendationTypeId = recommendationTypeId,
                        WorkItem = ToWorkItem(updated)
                    };
                }
                else
                {
                    _logger.LogWarning("Cannot find existing parent work item.");
                }
            }

            try
            {
                JsonPatchDocument workItemPatch = new JsonPatchDocument
                {
                    new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Title", Value = $"Retirement Tracking: {representativeAdvisory.Properties.ShortDescription.Problem}"},
                    new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Tags", Value = $"{parentLabel};{_advisoryLabel};{_advisoryParentLabel};{representativeAdvisory.Properties.Impact.ToLower()}"},
                    new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.State", Value = _workItemOpenState},
                    new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.AssignedTo", Value = _workItemDefaultAssignee},
                    new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Description", Value = GenerateParentWorkItemBody(representativeAdvisory, parentRepo)},
                };

                foreach (var kvp in childItemsByRepo)
                {
                    foreach (var wki in kvp.Value)
                    {
                        workItemPatch.Add(new JsonPatchOperation
                        {
                            Operation = Operation.Add,
                            Path = "/relations/-",
                            Value = new
                            {
                                rel = "System.LinkTypes.Hierarchy-Forward",
                                url = $"{_credentialProvider.GetConnectionUri()}/{kvp.Key}/_apis/wit/workItems/{wki.Id}",
                                attributes = new
                                {
                                    comment = "Auto-linked by Retirebot"
                                }
                            }
                        });
                    }
                }

                int childWorkItemCount = childItemsByRepo.Values.Sum(i => i.Count);

                if (whatIf)
                {
                    _logger.LogInformation("[WhatIf] Would create a new parent work item for recommendation {TypeId} with {Count} child work items",
                        recommendationTypeId, childWorkItemCount);

                    return new ParentWorkItemResult()
                    {
                        Action = ParentWorkItemAction.Created,
                        ChildCount = childWorkItemCount,
                        RecommendationTypeId = recommendationTypeId,
                        WorkItem = CreateWhatIf(workItemPatch[0].Value.ToString()!, workItemPatch[4].Value.ToString() ?? string.Empty, [parentLabel, _advisoryParentLabel], [_workItemDefaultAssignee])
                    };
                }

                var workItem = await workItemClient.CreateWorkItemAsync(workItemPatch, parentRepo, _workItemType);

                _logger.LogInformation("Successfully created parent tracking work item for {advisoryName}, {workItemID}", representativeAdvisory.Name, workItem.Id);

                return new ParentWorkItemResult()
                {
                    Action = ParentWorkItemAction.Created,
                    ChildCount = childWorkItemCount,
                    RecommendationTypeId = recommendationTypeId,
                    WorkItem = ToWorkItem(workItem)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Work Item for advisory {AdvisoryId}", representativeAdvisory.Name);
            }
            return null;
        }

        [GeneratedRegex(@"<h3>Last Updated<\/h3>\n<code>(.*)+<\/code>")]
        private static partial Regex LastUpdatedFormat();

        private record ChildWorkItemReference(string ProjectName, string WorkItemId);
        private static string SanitiseWiQLInput(string variable) => variable.Replace("'", "''");
    }
}