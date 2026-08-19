using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Contracts.V2026_06_25
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ParentWorkItemAction
    {
        Created,
        Updated,
        Unchanged
    }

    public class ParentWorkItemResult
    {
        [JsonPropertyName("workItem")] public required WorkItem WorkItem { get; set; }
        [JsonPropertyName("action")] public ParentWorkItemAction Action { get; set; }
        [JsonPropertyName("recommendationTypeId")] public string RecommendationTypeId { get; set; } = string.Empty;
        [JsonPropertyName("childCount")] public int ChildCount { get; set; }
    }
}