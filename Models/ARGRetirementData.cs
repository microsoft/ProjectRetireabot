using System.Text.Json.Serialization;

namespace Retirebot.Models
{
    public class ARGRetirementData
    {
        [JsonPropertyName("ServiceID")]
        public required string ServiceID { get; set; }
        [JsonPropertyName("id")]
        public required string Id { get; set; }
        [JsonPropertyName("location")]
        public required string Location { get; set; }
        [JsonPropertyName("resourceGroup")]
        public required string ResourceGroup { get; set; }
        [JsonPropertyName("resourceId")]
        public required string ResourceId { get; set; }
        [JsonPropertyName("subscriptionId")]
        public required string SubscriptionId { get; set; }
    }
}
