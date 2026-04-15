using System.Text.Json.Serialization;

namespace Retirebot.Models.Azure
{
    public class QueryResult<T>
    {
        [JsonPropertyName("count")]
        public int Length { get; set; }

        [JsonPropertyName("data")]
        public List<T> Data { get; set; } = new();

        [JsonPropertyName("resultTruncated")]
        public string? ResultTruncated { get; set; }

        [JsonPropertyName("totalRecords")]
        public int TotalRecords { get; set; }
    }
}
