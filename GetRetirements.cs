using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Retirebot.Models;
using System;

namespace Retirebot
{
    public class GetRetirements
    {
        private readonly ILogger _logger;
        private readonly ManagementClient _managementClient;

        public GetRetirements(ILoggerFactory loggerFactory, ManagementClient client)
        {
            _logger = loggerFactory.CreateLogger<GetRetirements>();
            _managementClient = client;
        }

        [Function("GetRetirements")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] TimerInfo myTimer)
        {
            //// Use _armClient to run Resource Graph queries
            //// Example: var response = await _armClient.GetTenantResourceGraphAsync(content);

            //_logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            
            //if (myTimer.ScheduleStatus is not null)
            //{
            //    _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            //}

            var subs = await _managementClient.GetSubscriptionsAsync();
            QueryResult<ARGRetirementData> data = await _managementClient.RunQueryAsync(subs[0], "advisorresources | where properties.extendedProperties.recommendationSubCategory == \"ServiceUpgradeAndRetirement\" | where tostring(properties.category) has \"HighAvailability\" | extend resourceId = tostring(properties.resourceMetadata.resourceId) | project id, subscriptionId, resourceGroup, location, resourceId, ServiceID = tostring(properties.recommendationTypeId)");

            for (int i = 0; i < data.Length; i++)
            {
                var advisoryId = data.Data[i].Id;
                var advisory = await _managementClient.GetAdvisoryAsync(advisoryId);

                return new OkObjectResult(advisory);
            }

            return new OkObjectResult(data);
        }
    }
}
