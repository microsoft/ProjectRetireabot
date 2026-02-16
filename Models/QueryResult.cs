using System.Text.Json.Serialization;

namespace Retirebot.Models
{
    public class QueryResult<T>
    {
        [JsonPropertyName("count")]
        public int Length { get; set; }
        [JsonPropertyName("data")]
        public List<T> Data { get; set; }
        [JsonPropertyName("resultsTrauncated")]
        public bool TrauncatedResults { get; set; }
        [JsonPropertyName("totalRecords")]
        public int TotalRecords { get; set; }
    }
}
