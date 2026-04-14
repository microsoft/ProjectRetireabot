using Retirebot.Models;
using Retirebot.Models.Azure;

namespace Retirebot.Helpers
{
    public interface IIssueClient
    {
        Task<Dictionary<string, WorkItem>> FindExistingByAdvisoryAsync(List<Advisory> advisories, string targetRepo);
        Task<List<(Advisory, WorkItem)>> CreateBatchAsync(List<Advisory> advisories, string targetRepo, bool assignCopilot);
        Task<WorkItem?> FindOrCreateParentAsync(string recommendationTypeId, Advisory representativeAdvisory, Dictionary<string, List<WorkItem>> childItemsByRepo, string parentRepo);
    }
}