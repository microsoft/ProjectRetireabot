using Microsoft.Extensions.Logging;
using Octokit;
using Retirebot.Models;
using System.Text.RegularExpressions;

namespace Retirebot.Helpers
{
    public partial class GitHubHelper
    {
        public async static Task<Dictionary<string, Issue>> FindExistingIssuesByLabelsAsync(ILogger logger, GitHubClient ghClient, List<Advisory> advisories)
        {
            string? targetRepo = Environment.GetEnvironmentVariable("TARGET_REPOSITORY");

            if (targetRepo == null)
            {
                throw new MissingFieldException("TARGET_REPOSITORY is empty ");
            }

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

        public async static Task<List<Issue>> CreateIssuesBatch(ILogger logger, GitHubClient ghClient, List<Advisory> advisories)
        {
            string? targetRepo = Environment.GetEnvironmentVariable("TARGET_REPOSITORY");

            if (targetRepo == null)
            {
                throw new MissingFieldException("TARGET_REPOSITORY is empty ");
            }

            SemaphoreSlim semaphore = new SemaphoreSlim(5);

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

                    newIssue.Assignees.Add("copilot-swe-agent[bot]");

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
   return new List<Issue>();             
            }
 return [.. results.Where(i => i != null).Select(i => i!)];
        }

        private const int MaxLabelLength = 50;
        private const string AdvisoryLabelPrefix = "advisor-";

        private static string GetAdvisoryLabel(string advisoryName)
        {
            var label = $"{AdvisoryLabelPrefix}{advisoryName}";
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
    }
}
