using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Contracts.V2026_06_25
{
    public class Advisory
    {
        [JsonPropertyName("id")] public required string Id { get; init; }
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("type")] public required string Type { get; init; }
        [JsonPropertyName("subscriptionId")] public required string SubscriptionId { get; init; }
        [JsonPropertyName("resourceGroup")] public required string ResourceGroup { get; init; }
        [JsonPropertyName("resourceName")] public required string ResourceName { get; init; }
        [JsonPropertyName("category")] public required string Category { get; init; }
        [JsonPropertyName("impact")] public required string Impact { get; init; }
        [JsonPropertyName("retirementDate")] public string? RetirementDate { get; init; }
        [JsonPropertyName("retirementFeatureName")] public string? RetirementFeatureName { get; init; }
        [JsonPropertyName("lastUpdated")] public required DateTime LastUpdated { get; init; }
        [JsonPropertyName("problem")] public required string Problem { get; init; }
        [JsonPropertyName("solution")] public required string Solution { get; init; }
    }
}