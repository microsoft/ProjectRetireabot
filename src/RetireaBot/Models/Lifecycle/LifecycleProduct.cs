using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Models.Lifecycle
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LifecycleProduct
    {
        AksKubernetes,
        AzurePostgreSQLFlexible
    }
}