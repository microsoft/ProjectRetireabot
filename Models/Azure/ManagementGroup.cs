using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Models.Azure
{
    public class ManagementGroupDescendantsResponse
    {
        [JsonPropertyName("value")]
        public List<ManagementGroupDescendant> Value { get; set; } = [];
    }

    public class ManagementGroupDescendant
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("properties")]
        public DescendantProperties Properties { get; set; } = new();

        public bool IsSubscription => Type.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase);
    }

    public class DescendantProperties
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("parent")]
        public DescendantParent? Parent { get; set; }
    }

    public class DescendantParent
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}
