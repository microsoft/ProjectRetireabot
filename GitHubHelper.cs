using Microsoft.Extensions.Logging;
using Octokit;
using Retirebot.Models;

namespace Retirebot
{
    public class GitHubHelper
    {
        public async static Task<Dictionary<string, Issue>> FindExistingIssuesByLabelsAsync(ILogger logger, GitHubClient ghClient, List<Advisory> advisories)
        {
            Dictionary<string, Issue> existingIssues = new Dictionary<string, Issue>();
            const int batchSize = 5;

            for (int i = 0; i < advisories.Count; i += batchSize)
            {
                var batch = advisories.Skip(i).Take(batchSize).ToList();
                var repo = "ZanyLeonic/TestArchitecture";
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
    }
}
