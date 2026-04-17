using Retirebot.Models;
using Retirebot.Models.Azure;
using Retirebot.Models.HTTP;

namespace Retirebot.Helpers
{
    public interface IWorkItemClient
    {
        Task<Dictionary<string, WorkItem>> FindExistingByAdvisoryAsync(List<Advisory> advisories, string targetRepo);
        Task<List<(Advisory, WorkItem)>> CreateBatchAsync(List<Advisory> advisories, string targetRepo, bool assignCopilot, bool whatIf);
        Task<ParentWorkItemResult?> FindOrCreateParentAsync(string recommendationTypeId, Advisory representativeAdvisory, Dictionary<string, List<WorkItem>> childItemsByRepo, string parentRepo, bool whatIf);
    }
}