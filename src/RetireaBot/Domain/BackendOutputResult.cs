using Microsoft.RetireaBot.Models;

namespace Microsoft.RetireaBot.Domain
{
    public class BackendOutputResult
    {
        public required string BackendName { get; set; }      // "GitHub", "AzureDevOps", "PowerBI"…
        public required GetRetirementsResult Status { get; set; }
        public string? Error { get; set; }
        public List<WorkItem> Existing { get; set; } = [];
        public List<WorkItem> Created { get; set; } = [];
        public List<ParentWorkItemResult> Parents { get; set; } = [];
    }
}