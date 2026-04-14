using Microsoft.Extensions.Logging;
using Octokit;
using Retirebot.Models.Azure;
using System.Text.RegularExpressions;

namespace Retirebot.Helpers.GitHub
{
    public partial class IssueClient
    {
        private const int MaxLabelLength = 50;
        private const string AdvisoryLabelPrefix = "advisor-";
        private const string ParentLabelPrefix = "advisor-type-";

        public async static Task<Dictionary<string, Issue>> FindExistingIssuesByLabelsAsync(ILogger logger, GitHubClient ghClient, List<Advisory> advisories, string targetRepo)
        {
            Dictionary<string, Issue> existingIssues = new Dictionary<string, Issue>();
            const int batchSize = 5;

            for (int i = 0; i < advisories.Count; i += batchSize)
            {
                var batch = advisories.Skip(i).Take(batchSize).ToList();
                var labelQueries = batch.Select(a => GetAdvisoryLabel(a.Name));
                var searchQuery = $"repo:{targetRepo} label:{string.Join(",", labelQueries)}";

                var searchRequest = new SearchIssuesRequest(searchQuery)
                {
                    Type = IssueTypeQualifier.Issue,
                    Repos = new RepositoryCollection
                    {
                        targetRepo
                    }
                };

                try
                {
                    var results = await ghClient.Search.SearchIssues(searchRequest);

                    for (int j = 0; j < results.Items.Count; j++)
                    {
                        var issue = results.Items[j];
                        var advisoryLabel = issue.Labels.FirstOrDefault(l => l.Name.StartsWith(AdvisoryLabelPrefix));

                        if (advisoryLabel != null)
                        {

                            var matchedAdvisory = batch.FirstOrDefault(a => GetAdvisoryLabel(a.Name) == advisoryLabel.Name);
                            if (matchedAdvisory != null)
                            {
                                existingIssues[matchedAdvisory.Name] = issue;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to search for exisiting issues in batch.");
                }

                // Respect GitHub search rate limits (30 requests/minute)
                if (i + batchSize < advisories.Count)
                {
                    await Task.Delay(2000); // 2 second delay between batches
                }
            }
            return existingIssues;
        }

        public async static Task<List<(Advisory, Issue)>> CreateIssuesBatch(ILogger logger, CredentialProvider ghProvider, List<Advisory> advisories, string targetRepo, bool assignGHCP)
        {
            SemaphoreSlim semaphore = new SemaphoreSlim(5);
            
            GitHubClient? ghClient = ghProvider.GetCopilotCapableClient();
            if (ghClient == null)
            {
                ghClient = await ghProvider.GetPrimaryClient();
                logger.LogWarning("Attempting to use a non-CoPilot capable client, issue creation may fail");
            }
            
            var created = advisories.Select(async advisory =>
            {
                await semaphore.WaitAsync();

                try
                {
                    var newIssue = new NewIssue(GenerateIssueTitle(advisory))
                    {
                        Body = GenerateIssueBody(advisory)
                    };

                    // Add labels including the advisory GUID
                    newIssue.Labels.Add(GetAdvisoryLabel(advisory.Name));
                    newIssue.Labels.Add("azure-advisor");
                    newIssue.Labels.Add(advisory.Properties.Impact.ToLower());

                    if (assignGHCP) newIssue.Assignees.Add("copilot-swe-agent[bot]");

                    string[] repoParts = targetRepo.Split("/");

                    var created = await ghClient.Issue.Create(repoParts[0], repoParts[1], newIssue);
                    logger.LogInformation("Created issue #{Number} for advisory {AdvisoryId}",
                        created.Number, advisory.Name);

                    return created;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create issue for advisory {AdvisoryId}", advisory.Name);
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
                return new List<(Advisory, Issue)>();
            }
            return [.. results.Select((r, i) => (advisory: advisories[i], issue: r))
                  .Where(p => p.issue != null)
                  .Select(p => (p.advisory, p.issue!))];
        }

        private static string GetAdvisoryLabel(string advisoryName)
        {
            var label = $"{AdvisoryLabelPrefix}{advisoryName}";
            if (label.Length > MaxLabelLength)
            {
                label = label[..MaxLabelLength];
            }
            return label;
        }

        private static string GetParentLabel(string recommendationTypeId)
        {
            var label = $"{ParentLabelPrefix}{recommendationTypeId}";
            if (label.Length > MaxLabelLength)
            {
                label = label[..MaxLabelLength];
            }
            return label;
        }

        private static string GenerateIssueTitle(Advisory advisory)
        {
            return $"{advisory.Properties.ShortDescription.Problem} - {advisory.Properties.ImpactedValue}";
        }

        private static string GenerateIssueBody(Advisory advisory)
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
        private static (HashSet<string>, int, int) ParseTaskListReferences(string body)
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

        /// <summary>
        /// Builds the full cross-repo issue reference (e.g., "owner/repo#42").
        /// If the child is in the same repo as the parent, uses short form "#42".
        /// </summary>
        private static string GetIssueReference(Issue childIssue, string childRepo, string parentRepo)
        {
            return string.Equals(childRepo, parentRepo, StringComparison.OrdinalIgnoreCase)
                ? $"#{childIssue.Number}"
                : $"{childRepo}#{childIssue.Number}";
        }

        private static string GenerateParentIssueBody(Advisory representativeAdvisory, Dictionary<string, List<Issue>> childIssuesByRepo, string parentRepo)
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

        public static async Task<Issue?> FindOrCreateParentIssueAsync(ILogger logger, GitHubClient ghClient, string recommendationTypeId, Advisory recommendationInfo, Dictionary<string, List<Issue>> childIssuesByRepo, string parentRepo)
        {
            string parentLabel = GetParentLabel(recommendationTypeId);
            string[] repoParts = parentRepo.Split("/");
            Issue? existingParent = null;

            SearchIssuesRequest searchRequest = new SearchIssuesRequest($"repo:{parentRepo} label:{parentLabel}")
            {
                Type = IssueTypeQualifier.Issue,
                Repos = new RepositoryCollection
                    {
                        parentRepo
                    }
            };

            try
            {
                SearchIssuesResult results = await ghClient.Search.SearchIssues(searchRequest);
                existingParent = results.Items.FirstOrDefault(i =>
                    i.Labels.Any(l => l.Name == parentLabel)
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to search for existing parent issue.");
            }

            if (existingParent != null)
            {
                (HashSet<string> existingRefs, _, _) = ParseTaskListReferences(existingParent.Body);

                List<string> allRefs = childIssuesByRepo.SelectMany(kvp => kvp.Value.Select(issue => GetIssueReference(issue, kvp.Key, parentRepo))).ToList();
                List<string> newRefs = allRefs.Where(r => !existingRefs.Contains(r)).ToList();

                if (newRefs.Count == 0)
                {
                    logger.LogInformation("Parent issue #{Number} for recommendation {TypeId} is already up to date", existingParent.Number, recommendationTypeId);
                    return existingParent;
                }

                var existingTaskLines = TaskListPattern().Matches(existingParent.Body).Select(m => m.Value.Trim());
                var allTaskLines = existingTaskLines.Concat(newRefs.Select(r => $"- [ ] {r}"));
                string newSection = $"### Affected Resources ({allRefs.Count})\n{string.Join("\n", allTaskLines)}\n";

                string updatedBody = AffectedResourcesSectionPattern().Replace(existingParent.Body, newSection);
                if (updatedBody == existingParent.Body)
                    updatedBody += $"\n{newSection}";

                updatedBody = LastUpdatedFormat().Replace(updatedBody, $"Last Updated\n`{DateTime.UtcNow:r}`");

                IssueUpdate update = new IssueUpdate { Body = updatedBody };

                // Reopen if it was closed, since new resources appeared
                if (existingParent.State == ItemState.Closed)
                {
                    update.State = ItemState.Open;
                    logger.LogInformation("Reopening parent issue #{Number} — new affected resources found", existingParent.Number);
                }

                Issue updated = await ghClient.Issue.Update(repoParts[0], repoParts[1], existingParent.Number, update);
                logger.LogInformation("Updated parent issue #{Number} with {Count} new child references for recommendation {TypeId}",
                    updated.Number, newRefs.Count, recommendationTypeId);

                return updated;
            }

            logger.LogInformation("Cannot find existing parent, creating new parent issue for {RecommendationTypeId}...", recommendationTypeId);

            try
            {
                NewIssue newIssue = new NewIssue($"Retirement Tracking: {recommendationInfo.Properties.ShortDescription.Problem}")
                {
                    Body = GenerateParentIssueBody(recommendationInfo, childIssuesByRepo, parentRepo)
                };

                // Add labels including the advisory GUID
                newIssue.Labels.Add(parentLabel);
                newIssue.Labels.Add("azure-advisor");
                newIssue.Labels.Add("tracking");
                newIssue.Labels.Add(recommendationInfo.Properties.Impact.ToLower());

                var created = await ghClient.Issue.Create(repoParts[0], repoParts[1], newIssue);
                logger.LogInformation("Created parent issue #{Number} for recommendation {TypeId} with {Count} child issues",
                    created.Number, recommendationTypeId, childIssuesByRepo.Values.Sum(i => i.Count));

                return created;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create issue for advisory {AdvisoryId}", recommendationInfo.Name);
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
