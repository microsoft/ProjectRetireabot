using Microsoft.RetireaBot.Models.Azure;
using System.Net.Http.Json;
using System.Text.Json;

namespace Microsoft.RetireaBot.Helpers.Azure
{
    public class ManagementClient
    {
        private HttpClient _client;

        public ManagementClient(HttpClient client)
        {
            _client = client;
        }
        public virtual async Task<string[]> GetSubscriptionsAsync()
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

        public virtual async Task<QueryResult<RetirementData>> RunQueryAsync(string subscriptionId, string query)
        {
            string uri = "/providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01";

            var requestBody = new
            {
                subscriptions = new[] { subscriptionId },
                query
            };

            var response = await _client.PostAsJsonAsync(uri, requestBody);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<QueryResult<RetirementData>>();

            if (result == null)
            {
                throw new InvalidOperationException("Got a null object when attempting to deserialise Azure Resource Graph Query response.");
            }

            return result;
        }

        public virtual async Task<Advisory> GetAdvisoryAsync(string uri)
        {
            string apiVersion = "2025-01-01";

            var response = await _client.GetAsync(string.Format("{0}?api-version={1}", uri, apiVersion));
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<Advisory>();

            if (result == null)
            {
                throw new InvalidOperationException("Got a null object when attempting to deserialise Azure Advisory response.");
            }

            return result;
        }

        public virtual async Task<Dictionary<string, string>> GetManagementGroupSubscriptionsAsync(string groupId)
        {
            var response = await _client.GetAsync($"/providers/Microsoft.Management/managementGroups/{groupId}/descendants?api-version=2020-05-01");
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ManagementGroupDescendantsResponse>();

            if (result == null)
            {
                throw new InvalidOperationException($"Got a null object when attempting to deserialise Management Group descendants for {groupId}.");
            }

            return result.Value
                .Where(d => d.IsSubscription)
                .ToDictionary(d => d.Name, d => groupId);
        }
    }
}
