using System;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Retirebot
{
    public class GetRetirements
    {
        private readonly ILogger _logger;

        public GetRetirements(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GetRetirements>();
        }

        [Function("GetRetirements")]
        public void Run([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer)
        {
            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            
            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            }
        }
    }
}
