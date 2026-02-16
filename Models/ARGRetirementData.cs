using System.Text.Json.Serialization;

namespace Retirebot.Models
{
    public class ARGRetirementData
    {
        [JsonPropertyName("ServiceID")]
        public string ServiceID { get; set; }
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("location")]
        public string Location { get; set; }
        [JsonPropertyName("resourceGroup")]
        public string ResourceGroup { get; set; }
        [JsonPropertyName("resourceId")]
        public string ResourceId { get; set; }
        [JsonPropertyName("subscriptionId")]
        public string SubscriptionId { get; set; }
    }
}
