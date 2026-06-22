using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Models.Azure
{
    public class RetirementData
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
        [JsonPropertyName("retirementDate")]
        public string? RetirementDate { get; set; }
        [JsonPropertyName("retirementFeatureName")]
        public string? RetirementFeatureName { get; set; }
        [JsonPropertyName("maturityLevel")]
        public required string MaturityLevel { get; set; }
        [JsonPropertyName("recommendationOfferingId")]
        public required string RecommendationOfferingId { get; set; }
        [JsonPropertyName("shortDescriptionProblem")]
        public string? ShortDescriptionProblem { get; set; }
        [JsonPropertyName("shortDescriptionSolution")]
        public string? ShortDescriptionSolution { get; set; }

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
                ImpactedValue = !string.IsNullOrWhiteSpace(ImpactedValue) ? ImpactedValue : ExtractResourceName(ResourceId),
                LastUpdated = DateTime.TryParse(LastUpdated, out var dt) ? dt : DateTime.MinValue,
                RecommendationTypeId = ServiceID,
                ShortDescription = new ShortDescription
                {
                    Problem = !string.IsNullOrWhiteSpace(ShortDescriptionProblem) ? ShortDescriptionProblem
                        : $"{(string.IsNullOrWhiteSpace(RetirementFeatureName) ? "Azure service" : RetirementFeatureName)} is scheduled for retirement{(string.IsNullOrWhiteSpace(RetirementDate) ? "" : $" on {RetirementDate}")}",
                    Solution = !string.IsNullOrWhiteSpace(ShortDescriptionSolution) ? ShortDescriptionSolution
                        : $"Migrate away from {(string.IsNullOrWhiteSpace(RetirementFeatureName) ? "the retiring service" : RetirementFeatureName)} before the retirement date."
                },
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

        private static string ExtractResourceName(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId)) return string.Empty;
            int lastSlash = resourceId.LastIndexOf('/');
            return lastSlash >= 0 && lastSlash < resourceId.Length - 1
                ? resourceId[(lastSlash + 1)..]
                : resourceId;
        }
    }
}
