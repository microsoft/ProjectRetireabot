using System.Text.Json;
using System.Text.Json.Serialization;

namespace Retirebot.Models
{
    public class Advisory
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        [JsonPropertyName("properties")]
        public AdvisoryProperties Properties { get; set; } = new();

        public string GetResourceGroupName()
        {
            return Id.Split('/')[3];
        }

        public string GetResourceName()
        {
            return Properties.ImpactedValue.ToString();
        }
    }

    public class AdvisoryProperties
    {
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;
        [JsonPropertyName("impact")]
        public string Impact { get; set; } = string.Empty;
        [JsonPropertyName("impactedField")]
        public string ImpactedField { get; set; } = string.Empty;
        [JsonPropertyName("impactedValue")]
        public string ImpactedValue { get; set; } = string.Empty;
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }
        [JsonPropertyName("recommendationTypeId")]
        public string RecommendationTypeId { get; set; } = string.Empty;
        [JsonPropertyName("extendedProperties")]
        public ExtendedProperties ExtendedProperties { get; set; } = new();
        [JsonPropertyName("resourceMetadata")]
        public ResourceMetadata ResourceMetadata { get; set; } = new();
        [JsonPropertyName("shortDescription")]
        public ShortDescription ShortDescription { get; set; } = new();
    }

    public class ExtendedProperties
    {
        [JsonPropertyName("maturityLevel")]
        public string MaturityLevel { get; set; } = string.Empty;
        [JsonPropertyName("recommendationOfferingId")]
        public string RecommendationOfferingId { get; set; } = string.Empty;
        [JsonPropertyName("recommendationSubCategory")]
        public string RecommendationSubCategory { get; set; } = string.Empty;
        [JsonPropertyName("retirementDate")]
        public string? RetirementDate { get; set; }
        [JsonPropertyName("retirementFeatureName")]
        public string? RetirementFeatureName { get; set; }

        // Allow unexpected keys without breaking deserialization
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    }

    public class ResourceMetadata
    {
        [JsonPropertyName("resourceId")]
        public string ResourceId { get; set; } = string.Empty;
    }

    public class ShortDescription
    {
        [JsonPropertyName("problem")]
        public string Problem { get; set; } = string.Empty;
        [JsonPropertyName("solution")]
        public string Solution { get; set; } = string.Empty;
    }
}
