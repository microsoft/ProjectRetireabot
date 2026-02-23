using Microsoft.Extensions.Logging;
using Octokit;
using Retirebot.Models;

namespace Retirebot.Helpers
{
    public class GitHubHelper
    {
        public async static Task<Dictionary<string, Issue>> FindExistingIssuesByLabelsAsync(ILogger logger, GitHubClient ghClient, List<Advisory> advisories)
        {
            string? repoOwner = Environment.GetEnvironmentVariable("REPOSITORY_OWNER");
            string? repoName = Environment.GetEnvironmentVariable("REPOSITORY_NAME");

            if (repoOwner == null || repoName == null)
            {
                throw new MissingFieldException("REPOSITORY_OWNER or REPOSITORY_NAME field are empty");
            }

            Dictionary<string, Issue> existingIssues = new Dictionary<string, Issue>();
            const int batchSize = 5;

            for (int i = 0; i < advisories.Count; i += batchSize)
            {
                var batch = advisories.Skip(i).Take(batchSize).ToList();
                var repo = $"{repoOwner}/{repoName}";
                var labelQueries = batch.Select(a => $"advisor-{a.Name}");
                var searchQuery = $"repo:{repo} label:{string.Join(",", labelQueries)}";

                var searchRequest = new SearchIssuesRequest(searchQuery)
                {
                    Type = IssueTypeQualifier.Issue,
                    Repos = new RepositoryCollection
                    {
                        repo
                    }
                };

                try
                {
                    var results = await ghClient.Search.SearchIssues(searchRequest);

                    for (int j = 0; j < results.Items.Count; j++)
                    {
                        var issue = results.Items[j];
                        var advisoryLabel = issue.Labels.FirstOrDefault(l => l.Name.StartsWith("advisor-"));

                        if (advisoryLabel != null)
                        {
                            var advisoryId = advisoryLabel.Name.Replace("advisor-", "");
                            existingIssues[advisoryId] = issue;
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
            string? repoOwner = Environment.GetEnvironmentVariable("REPOSITORY_OWNER");
            string? repoName = Environment.GetEnvironmentVariable("REPOSITORY_NAME");

            if (repoOwner == null || repoName == null)
            {
                throw new MissingFieldException("REPOSITORY_OWNER or REPOSITORY_NAME field are empty");
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
                    newIssue.Labels.Add($"advisor-{advisory.Name}");
                    newIssue.Labels.Add("azure-advisor");
                    newIssue.Labels.Add(advisory.Properties.Impact.ToLower());

                    newIssue.Assignees.Add("copilot-swe-agent[bot]");

                    var created = await ghClient.Issue.Create(repoOwner, repoName, newIssue);
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
