using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;
using Microsoft.RetireaBot.Models.HTTP;

namespace Microsoft.RetireaBot.Helpers
{
    public interface IWorkItemClient
    {
        Task<Dictionary<string, WorkItem>> FindExistingByAdvisoryAsync(List<Advisory> advisories, string targetRepo);
        Task<List<(Advisory, WorkItem)>> CreateBatchAsync(List<Advisory> advisories, string targetRepo, bool assignCopilot, bool whatIf);
        Task<ParentWorkItemResult?> FindOrCreateParentAsync(string recommendationTypeId, Advisory representativeAdvisory, Dictionary<string, List<WorkItem>> childItemsByRepo, string parentRepo, bool whatIf);
    }
}