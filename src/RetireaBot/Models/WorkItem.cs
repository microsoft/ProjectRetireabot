namespace Microsoft.RetireaBot.Models
{
    public enum WorkItemState
    {
        Open,
        Closed
    }

    public class WorkItem
    {
        public required string Id { get; set; }
        public required int Number { get; set; }
        public required string Title { get; set; }
        public string Body { get; set; } = string.Empty;
        public WorkItemState State { get; set; } = WorkItemState.Open;
        public List<string> Labels { get; set; } = [];
        public List<string> Assignees { get; set; } = [];
        public string? Url { get; set; }
    }
}