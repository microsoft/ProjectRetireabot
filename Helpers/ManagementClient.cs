using Retirebot.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Retirebot.Helpers
{
    public class ManagementClient
    {
        private HttpClient _client;

        public ManagementClient(HttpClient client)
        {
            _client = client;
        }
        public async Task<string[]> GetSubscriptionsAsync()
        {
            string uri = "/subscriptions?api-version=2022-12-01";

            var response = await _client.GetAsync(uri);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();

            return result.GetProperty("value")
                .EnumerateArray()
                .Select(s => s.GetProperty("subscriptionId").GetString() ?? string.Empty)
                .ToArray();
        }

        public async Task<QueryResult<ARGRetirementData>> RunQueryAsync(string subscriptionId, string query)
        {
            string uri = "/providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01";

            var requestBody = new
            {
                subscriptions = new[] { subscriptionId },
                query
            };

            var response = await _client.PostAsJsonAsync(uri, requestBody);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<QueryResult<ARGRetirementData>>();
            return result;
        }

        public async Task<Advisory> GetAdvisoryAsync(string uri)
        {
            string apiVersion = "2025-01-01";

            var response = await _client.GetAsync(string.Format("{0}?api-version={1}", uri, apiVersion));
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<Advisory>();

            return result;
        }
    }
}
