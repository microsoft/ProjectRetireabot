using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Domain
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GetRetirementsResult
    {
        Unknown,
        Success,
        Partial,
        Failure
    }
}