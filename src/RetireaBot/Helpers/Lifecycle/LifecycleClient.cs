using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Models.Lifecycle;

namespace Microsoft.RetireaBot.Helpers.Lifecycle
{
    public class LifecycleClient
    {
        private HttpClient _httpClient;
        private ILogger _logger;

        private const string AksVersionUrl = "https://github.com/MicrosoftDocs/azure-aks-docs/raw/refs/heads/main/articles/aks/supported-kubernetes-versions.md";
        private const string PostGresVersionUrl = "https://github.com/MicrosoftDocs/azure-databases-docs/raw/refs/heads/main/articles/postgresql/configure-maintain/concepts-version-policy.md";

        private readonly Dictionary<LifecycleProduct, (DateTime Expires, IReadOnlyList<LifecycleEntry> Entries)> _cache;
        private readonly TimeSpan _cacheTtl;

        public LifecycleClient(HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            _cache = new();
            _cacheTtl = TimeSpan.FromHours(12);

            _logger = loggerFactory.CreateLogger<LifecycleClient>();
            _httpClient = httpClient;
        }

        public async Task<IReadOnlyList<LifecycleEntry>> GetProductEntriesASync(LifecycleProduct product, string? overrideUrl = null)
        {
            if (_cache.TryGetValue(product, out var cached) && cached.Expires > DateTime.UtcNow)
            {
                return cached.Entries;
            }

            string url = overrideUrl ?? product switch
            {
                LifecycleProduct.AksKubernetes => AksVersionUrl,
                LifecycleProduct.AzurePostgreSQLFlexible => PostGresVersionUrl,
                _ => string.Empty,
            };

            try
            {
                _logger.LogInformation("Fetching lifecycle data for {Product}", product);

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string content = await response.Content.ReadAsStringAsync();

                IReadOnlyList<LifecycleEntry> entries = product switch
                {
                    LifecycleProduct.AksKubernetes => LifecycleParser.ParseAksVersionTable(content, url),
                    LifecycleProduct.AzurePostgreSQLFlexible => LifecycleParser.ParsePostgreSqlVersionTable(content, url),
                    _ => Array.Empty<LifecycleEntry>(),
                };

                _logger.LogInformation("Parsed {EntryCount} lifecycle entries for {Product}", entries.Count, product);
                _cache[product] = (DateTime.UtcNow.Add(_cacheTtl), entries);

                return entries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve lifecycle data for {Product}", product);
                return Array.Empty<LifecycleEntry>();
            }
        }
    }
}