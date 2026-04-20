using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.OAuth;
using Microsoft.VisualStudio.Services.WebApi;
using Retirebot.Models;
using Retirebot.Models.Azure;
using Retirebot.Models.HTTP;

namespace Retirebot.Helpers.AzureDevOps
{
    public class WorkItemClient : IWorkItemClient
    {
        private readonly VssConnection _vssConnection;
        private readonly WorkItemTrackingHttpClient _witClient;
        private readonly ILogger _logger;

        private readonly string _advisoryLabel;
        private readonly string _advisoryParentLabel;
        private readonly string _advisoryLabelPrefix;
        private readonly string _parentLabelPrefix;

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
        }

        private static Models.WorkItem ToWorkItem(Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem workItem)
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
                State = stateValue.Equals("Closed", StringComparison.OrdinalIgnoreCase)
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

        public Task<List<(Advisory, Models.WorkItem)>> CreateBatchAsync(List<Advisory> advisories, string targetRepo, bool assignCopilot, bool whatIf)
        {
            throw new NotImplementedException();
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

        public Task<ParentWorkItemResult?> FindOrCreateParentAsync(string recommendationTypeId, Advisory representativeAdvisory, Dictionary<string, List<Models.WorkItem>> childItemsByRepo, string parentRepo, bool whatIf)
        {
            throw new NotImplementedException();
        }
    }
}