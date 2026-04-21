using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.OAuth;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Retirebot.Models;
using Retirebot.Models.Azure;
using Retirebot.Models.HTTP;

namespace Retirebot.Helpers.AzureDevOps
{
    public partial class WorkItemClient : IWorkItemClient
    {
        private readonly VssConnection _vssConnection;
        private readonly WorkItemTrackingHttpClient _witClient;
        private readonly ILogger _logger;

        private readonly string _advisoryLabel;
        private readonly string _advisoryParentLabel;
        private readonly string _advisoryLabelPrefix;
        private readonly string _parentLabelPrefix;

        private readonly string _workItemDefaultAssignee;
        private readonly string _workItemOpenState;
        private readonly string _workItemClosedState;
        private readonly string _workItemType;

        private const int MaxLabelLength = 50;

        public WorkItemClient(IConfiguration config, DefaultAzureCredential credential, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<WorkItemClient>();

            string organisationUrl = config.GetSection(ConfigKeys.AzureDevOps.OrganisationUrl).Get<string>()
                           ?? throw new InvalidOperationException("AzureDevOps:OrganisationUrl is not configured.");

            var token = credential.GetToken(
                new TokenRequestContext(new[] { "499b84ac-1321-427f-aa17-267ca6975798/.default" }));

            _logger.LogInformation("Token acquired for Azure DevOps, expires: {Expiry}", token.ExpiresOn);

            var vssCredentials = new VssOAuthAccessTokenCredential(token.Token);
            _vssConnection = new VssConnection(new Uri(organisationUrl), vssCredentials);

            _witClient = _vssConnection.GetClient<WorkItemTrackingHttpClient>();

            _advisoryLabel = config.GetSection(ConfigKeys.App.AdvisoryLabel).Get<string>() ?? "azure-advisor";
            _advisoryParentLabel = config.GetSection(ConfigKeys.App.AdvisoryParentLabel).Get<string>() ?? "tracking";
            _advisoryLabelPrefix = config.GetSection(ConfigKeys.App.AdvisoryLabelPrefix).Get<string>() ?? "advisor-";
            _parentLabelPrefix = config.GetSection(ConfigKeys.App.ParentLabelPrefix).Get<string>() ?? "advisor-type-";

            _workItemDefaultAssignee = config.GetSection(ConfigKeys.AzureDevOps.WorkItemDefaultAssignee).Get<string>() ?? "";
            _workItemOpenState = config.GetSection(ConfigKeys.AzureDevOps.WorkItemOpenState).Get<string>() ?? "New";
            _workItemClosedState = config.GetSection(ConfigKeys.AzureDevOps.WorkItemClosedState).Get<string>() ?? "Closed";
            _workItemType = config.GetSection(ConfigKeys.AzureDevOps.WorkItemType).Get<string>() ?? "Task";
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

        private string GetAdvisoryLabel(string advisoryName)
        {
            var label = $"{_advisoryLabelPrefix}{advisoryName}";
            if (label.Length > MaxLabelLength)
            {
                label = label[..MaxLabelLength];
            }
            return label;
        }

        private string GetParentLabel(string recommendationTypeId)
        {
            var label = $"{_parentLabelPrefix}{recommendationTypeId}";
            if (label.Length > MaxLabelLength)
            {
                label = label[..MaxLabelLength];
            }
            return label;
        }


        /// <summary>
        /// Builds the full cross-repo issue reference (e.g., "owner/repo#42").
        /// If the child is in the same repo as the parent, uses short form "#42".
        /// </summary>
        private static string GetWorkItemReference(Models.WorkItem childIssue, string childRepo, string parentRepo)
        {
            return string.Equals(childRepo, parentRepo, StringComparison.OrdinalIgnoreCase)
                ? $"#{childIssue.Number}"
                : $"{childRepo}#{childIssue.Number}";
        }

        /// <summary>
        /// Generates the body for the parent issue, with the description of the issue and references to the child issues.
        /// </summary>
        private string GenerateParentWorkItemBody(Advisory representativeAdvisory, Dictionary<string, List<Models.WorkItem>> childIssuesByRepo, string parentRepo)
        {
            var props = representativeAdvisory.Properties;
            var taskListLines = childIssuesByRepo
                .SelectMany(kvp => kvp.Value.Select(issue => $"- [ ] {GetIssueReference(issue, kvp.Key, parentRepo)}"))
                .ToList();

            List<string> detailsList = new List<string>();

            if (!String.IsNullOrEmpty(props.ExtendedProperties?.RetirementDate))
                detailsList.Add($"**Retirement Date:** {props.ExtendedProperties?.RetirementDate}");

            if (!String.IsNullOrEmpty(props.ExtendedProperties?.RetirementFeatureName))
                detailsList.Add($"**Retirement Feature:** {props.ExtendedProperties?.RetirementFeatureName}");


            return $@"## Retirement Tracking: {props.ShortDescription.Problem}

**Impact:** {props.Impact}
**Category:** {props.Category}

{string.Join("\n", detailsList)}

### Description
{props.ShortDescription.Problem}

### Solution
{props.ShortDescription.Solution}

### Affected Resources ({taskListLines.Count})
{string.Join("\n", taskListLines)}

### Recommendation Type ID
`{props.RecommendationTypeId}`

### Last Updated
`{DateTime.UtcNow:r}`
";
        }

        private string GenerateWorkItemTitle(Advisory advisory)
        {
            return $"{advisory.Properties.ShortDescription.Problem} - {advisory.Properties.ImpactedValue}";
        }

        private string GenerateWorkItemBody(Advisory advisory)
        {
            var props = advisory.Properties;
            return $@"## Azure Advisor Recommendation

**Impact:** {props.Impact}
**Category:** {props.Category}
**Resource:** {props.ImpactedValue}

### Description
{props.ShortDescription.Problem}

### Solution
{props.ShortDescription.Solution}

### Details
- **Retirement Date:** {props.ExtendedProperties?.RetirementDate}
- **Retirement Feature:** {props.ExtendedProperties?.RetirementFeatureName}
- **Resource ID:** {props.ResourceMetadata?.ResourceId}
- **Last Updated:** {props.LastUpdated}

### Advisory ID
`{advisory.Name}`
";
        }


        /// <summary>
        /// Parses task list items from a GitHub issue body.
        /// Matches lines like: - [ ] owner/repo#123 or - [x] owner/repo#123
        /// </summary>
        private (HashSet<string>, int, int) ParseTaskListReferences(string body)
        {
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(body)) return (references, -1, -1);

            int startBlock = -1;
            int endBlock = -1;

            // Matches: - [ ] owner/repo#123 or - [x] owner/repo#123 or - [ ] #123
            var matches = TaskListPattern().Matches(body);
            foreach (Match match in matches)
            {
                references.Add(match.Groups["ref"].Value);
            }

            if (matches.Count > 0)
            {
                startBlock = matches[0].Index;
                var lastMatch = matches[^1];
                endBlock = lastMatch.Index + lastMatch.Length;
            }

            return (references, startBlock, endBlock);
        }

        public async Task<List<(Advisory, Models.WorkItem)>> CreateBatchAsync(List<Advisory> advisories, string targetRepo, bool assignCopilot, bool whatIf)
        {
            SemaphoreSlim semaphore = new SemaphoreSlim(5);

            var created = advisories.Select(async advisory =>
            {
                await semaphore.WaitAsync();

                try
                {
                    JsonPatchDocument workItemPatch = new JsonPatchDocument
                    {
                        new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Title", Value = GenerateWorkItemTitle(advisory)},
                        new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Tags", Value = $"{GetAdvisoryLabel(advisory.Name)};{_advisoryLabel};{advisory.Properties.Impact.ToLower()}"},
                        new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.State", Value = _workItemOpenState},
                        new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.AssignedTo", Value = _workItemDefaultAssignee},
                        new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Description", Value = GenerateWorkItemBody(advisory)},
                    };

                    if (whatIf)
                    {
                        throw new NotImplementedException();
                    }

                    var workItem = await _witClient.CreateWorkItemAsync(workItemPatch, targetRepo, _workItemType);

                    return ToWorkItem(workItem);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create work item for advisory {AdvisoryId}", advisory.Name);
                    return null;
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

            for (int i = 0; i < advisories.Count; i += batchSize)
            {
                var batch = advisories.Skip(i).Take(batchSize).ToList();
                var tagQueries = batch.Select(a => GetAdvisoryLabel(a.Name));

                var tagClauses = batch.Select(a => $"[System.Tags] CONTAINS '{GetAdvisoryLabel(a.Name)}'");
                var wiql = new Wiql
                {
                    Query = $"SELECT [System.Id] FROM WorkItems WHERE ({string.Join(" OR ", tagClauses)})"
                };

                var result = await _witClient.QueryByWiqlAsync(wiql, targetRepo);

                if (result.WorkItems.Any())
                {
                    var ids = result.WorkItems.Select(wi => wi.Id).ToList();
                    var workItems = await _witClient.GetWorkItemsAsync(ids, fields: ["System.Id", "System.Title", "System.Tags", "System.State", "System.Description", "System.AssignedTo"]);

                    foreach (var advisory in batch)
                    {
                        string advisoryTag = GetAdvisoryLabel(advisory.Name);

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
            return existingWorkItems;
        }

        public async Task<ParentWorkItemResult?> FindOrCreateParentAsync(string recommendationTypeId, Advisory representativeAdvisory, Dictionary<string, List<Models.WorkItem>> childItemsByRepo, string parentRepo, bool whatIf)
        {
            string parentLabel = GetParentLabel(recommendationTypeId);
            Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem? existingParent = null;

            var wiql = new Wiql
            {
                Query = $"SELECT [System.Id] FROM WorkItems WHERE [System.Tags] CONTAINS '{parentLabel}' OR CONTAINS '{_advisoryParentLabel}'"
            };

            var result = await _witClient.QueryByWiqlAsync(wiql, parentRepo);

            if (result.WorkItems.Any())
            {
                var wir = result.WorkItems.FirstOrDefault();

                if (wir != null)
                {
                    existingParent = await _witClient.GetWorkItemAsync(wir.Id, fields: ["System.Id", "System.Title", "System.Tags", "System.State", "System.Description", "System.AssignedTo"]);
                    var parsedParent = ToWorkItem(existingParent);


                    (HashSet<string> existingRefs, _, _) = ParseTaskListReferences(parsedParent.Body);

                    List<string> allRefs = childItemsByRepo.SelectMany(kvp => kvp.Value.Select(issue => GetWorkItemReference(issue, kvp.Key, parentRepo))).ToList();
                    List<string> newRefs = allRefs.Where(r => !existingRefs.Contains(r)).ToList();

                    if (newRefs.Count == 0)
                    {
                        _logger.LogInformation("Parent issue #{Number} for recommendation {TypeId} is already up to date", parsedParent.Number, recommendationTypeId);
                        return new ParentWorkItemResult()
                        {
                            Action = ParentWorkItemAction.Unchanged,
                            ChildCount = allRefs.Count,
                            RecommendationTypeId = recommendationTypeId,
                            WorkItem = ToWorkItem(existingParent)
                        };
                    }

                    var existingTaskLines = TaskListPattern().Matches(parsedParent.Body).Select(m => m.Value.Trim()).ToList();
                    var allTaskLines = existingTaskLines.Concat(newRefs.Select(r => $"- [ ] {r}"));
                    string newSection = $"### Affected Resources ({allRefs.Count})\n{string.Join("\n", allTaskLines)}\n";

                    string updatedBody = AffectedResourcesSectionPattern().Replace(parsedParent.Body, newSection);
                    if (updatedBody == parsedParent.Body)
                        updatedBody += $"\n{newSection}";

                    updatedBody = LastUpdatedFormat().Replace(updatedBody, $"Last Updated\n`{DateTime.UtcNow:r}`");


                    JsonPatchDocument workItemPatch = new JsonPatchDocument
                        {
                            new JsonPatchOperation {Operation = Operation.Replace, Path = "/fields/System.Description", Value = updatedBody},
                        };

                    // Reopen if it was closed, since new resources appeared
                    if (parsedParent.State == WorkItemState.Closed)
                    {
                        workItemPatch.Add(new JsonPatchOperation { Operation = Operation.Replace, Path = "/fields/System.State", Value = _workItemOpenState });
                        _logger.LogInformation("Reopening parent issue #{Number} — new affected resources found", parsedParent.Number);
                    }

                    if (whatIf)
                    {
                        _logger.LogInformation("[WhatIf] Parent Issue #{Number} with {Count} (vs {ExistingCount}) new child references for recommendation {TypeId} would be updated", parsedParent.Number, newRefs.Count, existingTaskLines.Count, recommendationTypeId);

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

                    var updated = await _witClient.UpdateWorkItemAsync(workItemPatch, int.Parse(parsedParent.Id));

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
                    new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Tags", Value = $"{parentLabel};{_advisoryParentLabel}"},
                    new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.State", Value = _workItemOpenState},
                    new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.AssignedTo", Value = _workItemDefaultAssignee},
                    new JsonPatchOperation {Operation = Operation.Add, Path = "/fields/System.Description", Value = GenerateParentWorkItemBody(representativeAdvisory, childItemsByRepo, parentRepo)},
                };

                int childWorkItemCount = childItemsByRepo.Values.Sum(i => i.Count);

                if (whatIf)
                {
                    _logger.LogInformation("[WhatIf] Would create a new parent issue for recommendation {TypeId} with {Count} child issues",
                        recommendationTypeId, childWorkItemCount);

                    return new ParentWorkItemResult()
                    {
                        Action = ParentWorkItemAction.Created,
                        ChildCount = childWorkItemCount,
                        RecommendationTypeId = recommendationTypeId,
                        WorkItem = CreateWhatIf(workItemPatch[0].Value.ToString()!, workItemPatch[4].Value.ToString() ?? string.Empty, [parentLabel, _advisoryParentLabel], [_workItemDefaultAssignee])
                    };
                }

                var workItem = await _witClient.CreateWorkItemAsync(workItemPatch, parentRepo, _workItemType);

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

        [GeneratedRegex(@"- \[[ x]{1,2}\] (?<ref>([^\s]+)?#\d+)")]
        private static partial Regex TaskListPattern();

        [GeneratedRegex(@"### Affected Resources \(\d+\)(\n+(?:- \[[ x]{1,2}\] [^\n]+\n?)+)")]
        private static partial Regex AffectedResourcesSectionPattern();

        [GeneratedRegex(@"Last Updated\n`(.*)+`")]
        private static partial Regex LastUpdatedFormat();
    }
}