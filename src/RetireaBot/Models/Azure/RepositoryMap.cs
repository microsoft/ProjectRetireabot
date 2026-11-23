using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Models.Azure
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AzureContainerType
    {
        ResourceGroup,
        ManagementGroup,
        Subscription,
    }

    public class AzureRepositoryMap
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("type")]
        public AzureContainerType? Type { get; set; }
        [JsonPropertyName("target")]
        public required string Target { get; set; }
        [JsonPropertyName("scopeId")]
        public string? ScopeId { get; set; }
    }
}