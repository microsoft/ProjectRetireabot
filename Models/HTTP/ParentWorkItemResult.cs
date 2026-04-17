using System.Text.Json.Serialization;

namespace Retirebot.Models.HTTP
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
        public required WorkItem WorkItem { get; set; }
        public ParentWorkItemAction Action { get; set; }
        public string RecommendationTypeId { get; set; } = string.Empty;
        public int ChildCount { get; set; }
    }
}