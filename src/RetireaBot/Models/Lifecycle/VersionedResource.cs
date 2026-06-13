using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Models.Lifecycle
{
    public class VersionedResource
    {
        [JsonPropertyName("id")]
        public required string Id { get; set; }
        [JsonPropertyName("name")]
        public required string Name { get; set; }
        [JsonPropertyName("type")]
        public required string Type { get; set; }
        [JsonPropertyName("subscriptionId")]
        public required string SubscriptionId { get; set; }
        [JsonPropertyName("resourceGroup")]
        public required string ResourceGroup { get; set; }
        [JsonPropertyName("location")]
        public string? Location { get; set; }
        [JsonPropertyName("version")]
        public required string Version { get; set; }
    }
}