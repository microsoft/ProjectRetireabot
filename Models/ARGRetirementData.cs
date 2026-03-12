using System.Text.Json.Serialization;

namespace Retirebot.Models
{
    public class ARGRetirementData
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
        public required string Location { get; set; }
        [JsonPropertyName("resourceId")]
        public required string ResourceId { get; set; }
        [JsonPropertyName("ServiceID")]
        public required string ServiceID { get; set; }
        [JsonPropertyName("impact")]
        public required string Impact { get; set; }
        [JsonPropertyName("category")]
        public required string Category { get; set; }
        [JsonPropertyName("impactedField")]
        public required string ImpactedField { get; set; }
        [JsonPropertyName("impactedValue")]
        public required string ImpactedValue { get; set; }
        [JsonPropertyName("lastUpdated")]
        public required string LastUpdated { get; set; }
        [JsonPropertyName("problem")]
        public required string Problem { get; set; }
        [JsonPropertyName("solution")]
        public required string Solution { get; set; }
        [JsonPropertyName("retirementDate")]
        public string? RetirementDate { get; set; }
        [JsonPropertyName("retirementFeatureName")]
        public string? RetirementFeatureName { get; set; }
        [JsonPropertyName("maturityLevel")]
        public required string MaturityLevel { get; set; }
        [JsonPropertyName("recommendationOfferingId")]
        public required string RecommendationOfferingId { get; set; }

        public Advisory ToAdvisory() => new Advisory
        {
            Id = Id,
            Name = Name,
            Type = Type,
            Properties = new AdvisoryProperties
            {
                Category = Category,
                Impact = Impact,
                ImpactedField = ImpactedField,
                ImpactedValue = ImpactedValue,
                LastUpdated = DateTime.TryParse(LastUpdated, out var dt) ? dt : DateTime.MinValue,
                RecommendationTypeId = ServiceID,
                ShortDescription = new ShortDescription { Problem = Problem, Solution = Solution },
                ExtendedProperties = new ExtendedProperties
                {
                    MaturityLevel = MaturityLevel,
                    RecommendationOfferingId = RecommendationOfferingId,
                    RecommendationSubCategory = "ServiceUpgradeAndRetirement",
                    RetirementDate = RetirementDate,
                    RetirementFeatureName = RetirementFeatureName
                },
                ResourceMetadata = new ResourceMetadata { ResourceId = ResourceId }
            }
        };
    }
}
