using System.Text.Json.Serialization;

namespace Retirebot.Models.Azure
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
        public required string Name { get; set; }
        [JsonPropertyName("type")]
        public required AzureContainerType Type { get; set; }
        [JsonPropertyName("repository")]
        public required string Repository { get; set; }
    }
}