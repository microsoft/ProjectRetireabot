using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Contracts.V2026_06_25
{
    public enum WorkItemState
    {
        Open,
        Closed
    }

    public class WorkItem
    {
        [JsonPropertyName("id")] public required string Id { get; set; }
        [JsonPropertyName("number")] public required int Number { get; set; }
        [JsonPropertyName("title")] public required string Title { get; set; }
        [JsonPropertyName("state")] public WorkItemState State { get; set; } = WorkItemState.Open;
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}