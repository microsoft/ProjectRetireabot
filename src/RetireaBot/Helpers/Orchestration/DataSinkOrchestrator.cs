using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Domain;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;


namespace Microsoft.RetireaBot.Helpers.Orchestration
{
    public class DataSinkOrchestrator : IDataSinkOrchestrator
    {
        private readonly ILogger<DataSinkOrchestrator> _logger;
        private readonly IReadOnlyList<IDataSinkClient> _dataSinkClients;
        private readonly IVendorSettingsProvider _vendorSettings;
        private readonly Helpers.Azure.ManagementClient _managementClient;

        public DataSinkOrchestrator(
            ILoggerFactory loggerFactory,
            IEnumerable<IDataSinkClient> dataSinkClients,
            IVendorSettingsProvider vendorSettings,
            Helpers.Azure.ManagementClient managementClient)
        {
            _logger = loggerFactory.CreateLogger<DataSinkOrchestrator>();
            _dataSinkClients = dataSinkClients.ToList();
            _vendorSettings = vendorSettings;
            _managementClient = managementClient;
        }

        public async Task<IReadOnlyList<DataSinkOutputResult>> RunAsync(List<Advisory> advisories, bool whatIf, CancellationToken cancellationToken = default)
        {
            if (_dataSinkClients.Count < 1)
            {
                _logger.LogInformation("No data sink clients registered; skipping");
                return [];
            }

            var tasks = _dataSinkClients
               .Select(c => ProcessBackendAsync(c, advisories, whatIf, cancellationToken));

            return await Task.WhenAll(tasks);
        }

        private async Task<DataSinkOutputResult> ProcessBackendAsync(
           IDataSinkClient dataSinkClient,
           List<Advisory> advisories,
           bool whatIf,
           CancellationToken cancellationToken)
        {
            string datasinkName = dataSinkClient.Backend.ToString();

            var output = new DataSinkOutputResult
            {
                BackendName = datasinkName,
                Status = GetRetirementsResult.Success,
            };

            _logger.LogInformation("Processing datasink {DataSink}", datasinkName);

            try
            {
                return await dataSinkClient.PushAsync(advisories, whatIf, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backend {Backend} failed unexpectedly", datasinkName);
                output.Status = GetRetirementsResult.Failure;
                output.Error = ex.Message;
            }

            return output;
        }
    }
}