using Retirebot.Models.Azure;

namespace Retirebot.Models
{
    public enum GetRetirementsResult
    {
        Unknown,
        Success,
        Failure
    }

    public class GetRetirementsResponse
    {
        public GetRetirementsResult Result { get; set; }
        public string ResultDescription { get; set; } = string.Empty;
        public List<Advisory>? Advisories { get; set; }
        public List<WorkItem>? ExistingWorkItems { get; set; }
        public List<WorkItem>? NewWorkItems { get; set; }
        public double TimeElapsed { get; set; }
    }
}