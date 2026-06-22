using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;
using Microsoft.RetireaBot.Models.HTTP;

namespace Microsoft.RetireaBot.Helpers.Sinks
{
    public class NoOpDataSink : IDataSinkClient
    {
        private readonly ILogger _logger;

        public NoOpDataSink(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<NoOpDataSink>();
        }

        public DataSinkBackend Backend => DataSinkBackend.NoOp;

        public async Task<DataSinkOutputResult> PushAsync(IReadOnlyList<Advisory> advisories, bool whatIf, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"Advisory Count: {advisories.Count}; whatIf: {whatIf}");

            return new DataSinkOutputResult()
            {
                BackendName = "NoOp",
                Status = GetRetirementsResult.Success,
                PushedCount = advisories.Count
            };
        }
    }
}